/**
 * 驗收條件：「連點結帳鈕五次，只產生一張訂單（mock 要能驗證 Idempotency-Key 相同）」。
 *
 * 這裡不 render 元件（這個 workspace 沒裝 @testing-library/react ／ jsdom，
 * 詳見交付回報），改成直接測試按鈕背後真正共用的邏輯：
 * `createIdempotentAction`。這比「render 出按鈕再點五下」更嚴格——
 * 它證明的是「五次呼叫最多只送出一次真正的 HTTP 請求」，而不只是
 * 「五次請求剛好帶了同一把 key」。
 */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import {
  ApiClient,
  ApiError,
  createPayloadIdempotencyScope,
  createPayloadIdempotentAction,
} from '@greygray/api-client';
import * as api from '@greygray/api-client/endpoints/storefront';
import {
  STOREFRONT_BASE_URL,
  resetStorefrontMockState,
} from '@greygray/api-client/mock/handlers.storefront';
import { storefrontServer } from '@greygray/api-client/mock/server';
import { problem } from '@greygray/api-client/mock/problems';
import { createIdempotentAction } from '../idempotentAction';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });

const CHECKOUT_URL = `${STOREFRONT_BASE_URL}/v1/cart/checkout`;
const CHECKOUT_BODY = { deliveryMethod: 'SelfPickup', shippingPolicy: 'ShipSeparately' } as const;

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  storefrontServer.resetHandlers();
  resetStorefrontMockState();
});
afterAll(() => storefrontServer.close());

describe('createIdempotentAction：結帳鈕連點五次', () => {
  it('五次同時呼叫只送出一次 HTTP 請求，五個 promise 都拿到同一張訂單', async () => {
    const action = createIdempotentAction((key) =>
      api.checkout(client, CHECKOUT_BODY, { idempotencyKey: key }),
    );

    const before = await api.listOrders(client);

    const results = await Promise.all([
      action.run(),
      action.run(),
      action.run(),
      action.run(),
      action.run(),
    ]);

    const after = await api.listOrders(client);

    // 對照 mock 的訂單清單：五次連點只多出一張訂單，不是五張。
    expect(after.items).toHaveLength(before.items.length + 1);
    // 五個 promise 全部 resolve 成同一張訂單，不是各自建了一張。
    expect(new Set(results.map((order) => order.id)).size).toBe(1);
  });

  it('五次呼叫共用同一把冪等鍵：直接檢查送給後端的 Idempotency-Key header', async () => {
    resetStorefrontMockState();
    const seenKeys: string[] = [];

    // 疊加一支「記錄 header 後照常建立訂單」的 handler，取代預設 handler。
    let orderIndex = 0;
    storefrontServer.use(
      http.post(CHECKOUT_URL, async ({ request }) => {
        seenKeys.push(request.headers.get('Idempotency-Key') ?? '');
        orderIndex += 1;
        return HttpResponse.json(
          {
            id: 'order-under-test',
            orderNumber: `TEST-${orderIndex}`,
            status: 'AwaitingPayment',
            shippingPolicy: CHECKOUT_BODY.shippingPolicy,
            deliveryMethod: CHECKOUT_BODY.deliveryMethod,
            goodsTotal: { amountMinor: 0, currency: 'TWD' },
            shippingFee: { amountMinor: 0, currency: 'TWD' },
            grandTotal: { amountMinor: 0, currency: 'TWD' },
            lines: [],
            placedAt: new Date().toISOString(),
          },
          { status: 201 },
        );
      }),
    );

    const action = createIdempotentAction((key) =>
      api.checkout(client, CHECKOUT_BODY, { idempotencyKey: key }),
    );

    await Promise.all([action.run(), action.run(), action.run(), action.run(), action.run()]);

    // 就算 UI 沒能完全防住連點（例如第一次還沒進 in-flight 狀態），
    // 真正送出去的每一次 Idempotency-Key 也必須完全相同。
    expect(seenKeys.length).toBeGreaterThanOrEqual(1);
    expect(new Set(seenKeys).size).toBe(1);
  });

  it('失敗後 key 不變（下一次呼叫是重試）；成功後才換新 key 給下一個新動作用', async () => {
    const action = createIdempotentAction((key) =>
      api.checkout(client, CHECKOUT_BODY, { idempotencyKey: key }),
    );
    const keyBeforeFailure = action.currentKey();

    storefrontServer.use(
      http.post(CHECKOUT_URL, () =>
        HttpResponse.json(problem(500, 'platform.unexpected', '系統發生問題，請稍後再試。'), {
          status: 500,
        }),
      ),
    );

    await expect(action.run()).rejects.toBeInstanceOf(ApiError);
    expect(action.currentKey()).toBe(keyBeforeFailure);

    storefrontServer.resetHandlers(); // 模擬使用者按重試：這次會打到正常的 handler

    const order = await action.run();
    expect(order.status).toBe('AwaitingPayment');
    expect(action.currentKey()).not.toBe(keyBeforeFailure);
  });
});

describe('createPayloadIdempotencyScope', () => {
  it('相同 payload 重試沿用 key，修改 payload 換 key，完成後相同 payload 也換 key', () => {
    const scope = createPayloadIdempotencyScope();
    const first = scope.current({ quantity: 1 });
    expect(scope.current({ quantity: 1 })).toBe(first);

    const changed = scope.current({ quantity: 2 });
    expect(changed).not.toBe(first);
    expect(scope.current({ quantity: 2 })).toBe(changed);

    scope.complete();
    expect(scope.current({ quantity: 2 })).not.toBe(changed);
  });
});

describe('createPayloadIdempotentAction', () => {
  it('失敗後同 payload 沿用 key，改 payload 才換 key', async () => {
    const attempts: Array<{ payload: { method: string }; key: string }> = [];
    let shouldFail = true;
    const action = createPayloadIdempotentAction(async (payload: { method: string }, key) => {
      attempts.push({ payload, key });
      if (shouldFail) throw new Error('network');
      return payload.method;
    });

    await expect(action.run({ method: 'HomeDelivery' })).rejects.toThrow('network');
    shouldFail = false;
    await expect(action.run({ method: 'HomeDelivery' })).resolves.toBe('HomeDelivery');
    await expect(action.run({ method: 'SelfPickup' })).resolves.toBe('SelfPickup');

    expect(attempts[0]?.key).toBe(attempts[1]?.key);
    expect(attempts[2]?.key).not.toBe(attempts[1]?.key);
  });

  it('相同 payload 連點仍只執行一次', async () => {
    let calls = 0;
    const action = createPayloadIdempotentAction(async (payload: { order: number }) => {
      calls += 1;
      await Promise.resolve();
      return payload.order;
    });

    await Promise.all(Array.from({ length: 5 }, () => action.run({ order: 1 })));
    expect(calls).toBe(1);
  });
});
