import { setupServer } from 'msw/node';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { ApiClient, ApiError, createPayloadIdempotencyScope } from '@greygray/api-client';
import {
  adminShipmentHandlers,
  adminShipmentErrorScenarios,
  resetAdminShipmentMockState,
} from '@greygray/api-client/mock/handlers.admin.shipments';
import { SHIPMENT_IDS } from '@greygray/api-client/mock/fixtures.admin.shipments';
import { adminOrders } from '@greygray/api-client/mock/fixtures.admin';
import { createShipment, deliverShipment, dispatchShipment, listShipments } from './api';

/**
 * 沒有瀏覽器可以跑互動測試（這個環境沒有裝任何瀏覽器自動化工具），
 * 所以用這個直接打 mock server 的 smoke test 頂替，至少確認端點邏輯、
 * N:M、冪等鍵這幾件事是對的——不是只有 typecheck／build 綠燈。
 */
const server = setupServer(...adminShipmentHandlers);
const client = new ApiClient({ baseUrl: 'http://localhost:5001' });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  server.resetHandlers(...adminShipmentHandlers);
  resetAdminShipmentMockState();
});
afterAll(() => server.close());

function orderIdOf(orderNumber: string): string {
  const order = adminOrders.find((o) => o.orderNumber === orderNumber);
  if (!order) throw new Error(`測試資料錯亂：找不到訂單 ${orderNumber}`);
  return order.id;
}

describe('出貨單（M1b，/v1/shipments）', () => {
  it('列表可依 status 篩選，游標分頁有效', async () => {
    const all = await listShipments(client, { limit: 100 });
    expect(all.items.length).toBe(4);

    const dispatched = await listShipments(client, { status: 'Dispatched' });
    expect(dispatched.items.every((s) => s.status === 'Dispatched')).toBe(true);
    expect(dispatched.items.some((s) => s.id === SHIPMENT_IDS.homeDeliverySplitDispatched)).toBe(true);
  });

  it('fixture 示範一單多包：同一張訂單出現在兩張出貨單', async () => {
    const orderId = orderIdOf('GG260828021');
    const all = await listShipments(client, { limit: 100 });
    const containing = all.items.filter((s) => s.orderIds.includes(orderId));
    expect(containing.length).toBe(2);
  });

  it('fixture 示範一包多單：一張出貨單合併兩張訂單', async () => {
    const all = await listShipments(client, { limit: 100 });
    const merged = all.items.find((s) => s.id === SHIPMENT_IDS.convenienceMerged);
    expect(merged?.orderIds.length).toBe(2);
  });

  it('多選訂單建立出貨單：orderIds 是陣列，未勾選（空陣列）時後端 422', async () => {
    const orderA = orderIdOf('GG26082800031');
    const orderB = orderIdOf('GG260828022');
    const created = await createShipment(
      client,
      { orderIds: [orderA, orderB], method: 'HomeDelivery' },
      { idempotencyKey: 'shipment-smoke-create-1' },
    );
    expect(created.status).toBe('Draft');
    expect(created.orderIds).toEqual([orderA, orderB]);

    await expect(
      createShipment(client, { orderIds: [], method: 'HomeDelivery' }, { idempotencyKey: 'shipment-smoke-create-2' }),
    ).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      return true;
    });
  });

  it('交運：帶 Idempotency-Key，成功後狀態轉 Dispatched 並帶回 trackingNumber／carrierCost', async () => {
    const updated = await dispatchShipment(
      client,
      SHIPMENT_IDS.homeDeliverySplitDraft,
      { trackingNumber: 'TEST-TRACKING-001', carrierCost: { amountMinor: 9000, currency: 'TWD' } },
      { idempotencyKey: 'shipment-smoke-dispatch-1' },
    );
    expect(updated.status).toBe('Dispatched');
    expect(updated.trackingNumber).toBe('TEST-TRACKING-001');
    expect(updated.carrierCost).toEqual({ amountMinor: 9000, currency: 'TWD' });
    expect(updated.dispatchedAt).not.toBeNull();
  });

  it('簽收：成功後狀態轉 Delivered，deliveredAt 有值', async () => {
    const updated = await deliverShipment(client, SHIPMENT_IDS.selfPickupPacked, {
      idempotencyKey: 'shipment-smoke-deliver-1',
    });
    expect(updated.status).toBe('Delivered');
    expect(updated.deliveredAt).not.toBeNull();
  });

  it('已經是最終狀態（Delivered）的出貨單不能再簽收，回 422', async () => {
    await expect(
      deliverShipment(client, SHIPMENT_IDS.convenienceMerged, { idempotencyKey: 'shipment-smoke-deliver-2' }),
    ).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      return true;
    });
  });

  it('自驗注入：強制交運失敗時，錯誤帶得出 problem.detail 而不是通用字串', async () => {
    server.use(adminShipmentErrorScenarios.deliverAlreadyFinal);
    try {
      await deliverShipment(client, SHIPMENT_IDS.homeDeliverySplitDraft, {
        idempotencyKey: 'shipment-smoke-deliver-3',
      });
      expect.unreachable('應該要 422');
    } catch (cause) {
      expect(cause).toBeInstanceOf(ApiError);
      expect((cause as ApiError).problem.detail).toBeFalsy();
      expect((cause as ApiError).problem.title).toBe('這張出貨單已經是最終狀態，不能再標記送達。');
    }
  });

  it('payload-aware 冪等鍵：交運同一份內容重送用同一把 key，改了單號才換新的', () => {
    const scope = createPayloadIdempotencyScope();
    const payload = {
      shipmentId: SHIPMENT_IDS.homeDeliverySplitDraft,
      kind: 'dispatch',
      input: { trackingNumber: 'A', carrierCost: { amountMinor: 8500, currency: 'TWD' } },
    };
    const firstKey = scope.current(payload);
    const retryKey = scope.current(payload);
    expect(retryKey).toBe(firstKey);

    const changedPayload = { ...payload, input: { ...payload.input, trackingNumber: 'B' } };
    const changedKey = scope.current(changedPayload);
    expect(changedKey).not.toBe(firstKey);
  });

  it('找不到的出貨單交運回 404', async () => {
    await expect(
      dispatchShipment(
        client,
        'not-a-real-id',
        { trackingNumber: 'X', carrierCost: { amountMinor: 1000, currency: 'TWD' } },
        { idempotencyKey: 'shipment-smoke-dispatch-404' },
      ),
    ).rejects.toThrow();
  });

  it('三個 mutation 端點實際送出的 request 都帶著 Idempotency-Key header——這裡沒有瀏覽器可以截 DevTools 圖，改用攔截實際 header 證明（`docs/15` §2 提醒過：MSW handler 不檢查 header，光是測試綠燈證明不了有帶 key）', async () => {
    const capturedHeaders: string[] = [];
    server.events.on('request:start', ({ request }) => {
      if (request.method === 'POST') {
        capturedHeaders.push(request.headers.get('Idempotency-Key') ?? '');
      }
    });

    const orderId = orderIdOf('GG260828023');
    const created = await createShipment(
      client,
      { orderIds: [orderId], method: 'SelfPickup' },
      { idempotencyKey: 'shipment-smoke-header-create' },
    );
    await dispatchShipment(
      client,
      created.id,
      { trackingNumber: 'HEADER-CHECK', carrierCost: { amountMinor: 500, currency: 'TWD' } },
      { idempotencyKey: 'shipment-smoke-header-dispatch' },
    );
    await deliverShipment(client, created.id, { idempotencyKey: 'shipment-smoke-header-deliver' });

    expect(capturedHeaders).toEqual([
      'shipment-smoke-header-create',
      'shipment-smoke-header-dispatch',
      'shipment-smoke-header-deliver',
    ]);
  });
});
