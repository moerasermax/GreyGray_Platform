import { setupServer } from 'msw/node';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { ApiClient, ApiError, createPayloadIdempotencyScope } from '@greygray/api-client';
import {
  adminCompensationHandlers,
  adminCompensationErrorScenarios,
} from '@greygray/api-client/mock/handlers.admin.compensation';
import { markUnavailable, reportPriceChanged } from './api';

/**
 * 缺貨（unavailable）與漲價（price-changed）沒有瀏覽器可以跑互動測試，
 * 跟 `procurement.smoke.test.ts` 一樣直接打 mock server 頂替。
 */
const server = setupServer(...adminCompensationHandlers);
const client = new ApiClient({ baseUrl: 'http://localhost:5001' });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => server.resetHandlers(...adminCompensationHandlers));
afterAll(() => server.close());

describe('缺貨（M1b，FE-14）', () => {
  it('帶原因會成功', async () => {
    await expect(
      markUnavailable(
        client,
        'any-purchase-item-id',
        { reason: '現場架上已經賣完' },
        { idempotencyKey: 'compensation-smoke-1' },
      ),
    ).resolves.toBeUndefined();
  });

  it('沒填原因會 422', async () => {
    await expect(
      markUnavailable(client, 'any-purchase-item-id', { reason: '' }, { idempotencyKey: 'compensation-smoke-2' }),
    ).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      return true;
    });
  });

  it('自驗注入：後端拒絕缺貨標記時，錯誤帶得出 problem.detail 而不是通用字串', async () => {
    server.use(adminCompensationErrorScenarios.unavailableRejected);
    try {
      await markUnavailable(
        client,
        'any-purchase-item-id',
        { reason: '現場架上已經賣完' },
        { idempotencyKey: 'compensation-smoke-3' },
      );
      expect.unreachable('應該要 422');
    } catch (cause) {
      expect(cause).toBeInstanceOf(ApiError);
      expect((cause as ApiError).problem.detail).toBe(
        '這個品項已經回報買到，不能再標記缺貨，請重新整理清單。',
      );
    }
  });
});

describe('漲價詢問（M1b，FE-14）', () => {
  it('回報漲價會回 inquiryId 與 timeoutAt，且 timeoutAt 在未來', async () => {
    const before = Date.now();
    const result = await reportPriceChanged(
      client,
      'any-purchase-item-id',
      { newPrice: { amountMinor: 1_500, currency: 'JPY' } },
      { idempotencyKey: 'compensation-smoke-4' },
    );
    expect(result.inquiryId).toMatch(/^[0-9a-f]{32}$/);
    expect(new Date(result.timeoutAt).getTime()).toBeGreaterThan(before);
  });

  it('自驗注入：後端拒絕漲價回報時，錯誤帶得出 problem.detail 而不是通用字串', async () => {
    server.use(adminCompensationErrorScenarios.priceChangedRejected);
    try {
      await reportPriceChanged(
        client,
        'any-purchase-item-id',
        { newPrice: { amountMinor: 1_500, currency: 'JPY' } },
        { idempotencyKey: 'compensation-smoke-5' },
      );
      expect.unreachable('應該要 422');
    } catch (cause) {
      expect(cause).toBeInstanceOf(ApiError);
      expect((cause as ApiError).problem.detail).toBe(
        '這個品項已經被標記為缺貨，不能再回報漲價，請重新整理清單。',
      );
    }
  });

  it('對話框用的 payload-aware 冪等鍵：同一份漲價回報重送用同一把 key，改了金額才換新的', () => {
    const scope = createPayloadIdempotencyScope();
    const payload = { purchaseItemId: 'x', input: { newPrice: { amountMinor: 1_500, currency: 'JPY' } } };
    const firstKey = scope.current(payload);
    const retryKey = scope.current(payload);
    expect(retryKey).toBe(firstKey);

    const changedPayload = { ...payload, input: { newPrice: { amountMinor: 1_600, currency: 'JPY' } } };
    const changedKey = scope.current(changedPayload);
    expect(changedKey).not.toBe(firstKey);
  });
});
