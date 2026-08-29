import { setupServer } from 'msw/node';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { ApiClient, ApiError, createPayloadIdempotencyScope } from '@greygray/api-client';
import {
  adminProcurementHandlers,
  adminProcurementErrorScenarios,
  resetAdminProcurementMockState,
} from '@greygray/api-client/mock/handlers.admin.procurement';
import { CAMPAIGN_IDS } from '@greygray/api-client/mock/fixtures.admin';
import { PURCHASE_ITEM_IDS } from '@greygray/api-client/mock/fixtures.admin.procurement';
import { listPurchaseItems, reportPurchased } from './api';

/**
 * 沒有瀏覽器可以跑互動測試（這個環境沒有裝任何瀏覽器自動化工具），
 * 所以用這個直接打 mock server 的 smoke test 頂替，至少確認端點邏輯、
 * 422、冪等鍵這幾件事是對的——不是只有 typecheck／build 綠燈。
 */
const server = setupServer(...adminProcurementHandlers);
const client = new ApiClient({ baseUrl: 'http://localhost:5001' });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  server.resetHandlers(...adminProcurementHandlers);
  resetAdminProcurementMockState();
});
afterAll(() => server.close());

describe('採購清單（M1b-1）', () => {
  it('依 campaignId 讀清單，status 篩選有效', async () => {
    const all = await listPurchaseItems(client, CAMPAIGN_IDS.seoul);
    expect(all.length).toBe(3);

    const pending = await listPurchaseItems(client, CAMPAIGN_IDS.seoul, { status: 'Pending' });
    expect(pending.every((item) => item.status === 'Pending')).toBe(true);
    expect(pending.some((item) => item.id === PURCHASE_ITEM_IDS.pendingMask)).toBe(true);
  });

  it('全數買到會成功，狀態轉成 Purchased', async () => {
    const before = await listPurchaseItems(client, CAMPAIGN_IDS.seoul, { status: 'Pending' });
    const target = before.find((item) => item.id === PURCHASE_ITEM_IDS.pendingMask);
    expect(target).toBeDefined();

    await reportPurchased(
      client,
      PURCHASE_ITEM_IDS.pendingMask,
      {
        quantityPurchased: target!.quantityRequested,
        actualPaidOriginal: { amountMinor: 240_000, currency: 'JPY' },
        actualPaidBooking: { amountMinor: 5_400_0, currency: 'TWD' },
      },
      { idempotencyKey: 'procurement-smoke-1' },
    );

    const after = await listPurchaseItems(client, CAMPAIGN_IDS.seoul);
    const updated = after.find((item) => item.id === PURCHASE_ITEM_IDS.pendingMask);
    expect(updated?.status).toBe('Purchased');
    expect(updated?.quantityPurchased).toBe(target!.quantityRequested);
  });

  it('回報數量少於需求會 422，problem.detail 講清楚差在哪（這一版只能全數回報）', async () => {
    await expect(
      reportPurchased(
        client,
        PURCHASE_ITEM_IDS.pendingMask,
        {
          quantityPurchased: 1,
          actualPaidOriginal: { amountMinor: 100_000, currency: 'JPY' },
          actualPaidBooking: { amountMinor: 2_000_0, currency: 'TWD' },
        },
        { idempotencyKey: 'procurement-smoke-2' },
      ),
    ).rejects.toSatisfy((error: unknown) => {
      expect(error).toBeInstanceOf(ApiError);
      const apiError = error as ApiError;
      expect(apiError.status).toBe(422);
      expect(apiError.problem.detail).toBeTruthy();
      return true;
    });
  });

  it('記帳幣不是 TWD 會 422', async () => {
    const before = await listPurchaseItems(client, CAMPAIGN_IDS.seoul, { status: 'Pending' });
    const target = before.find((item) => item.id === PURCHASE_ITEM_IDS.pendingMask)!;
    await expect(
      reportPurchased(
        client,
        PURCHASE_ITEM_IDS.pendingMask,
        {
          quantityPurchased: target.quantityRequested,
          actualPaidOriginal: { amountMinor: 100_000, currency: 'JPY' },
          actualPaidBooking: { amountMinor: 2_000_0, currency: 'JPY' },
        },
        { idempotencyKey: 'procurement-smoke-3' },
      ),
    ).rejects.toThrow();
  });

  it('自驗注入：強制回報失敗時，錯誤帶得出 problem.detail 而不是通用字串', async () => {
    server.use(adminProcurementErrorScenarios.purchasedRejected);
    const before = await listPurchaseItems(client, CAMPAIGN_IDS.seoul, { status: 'Pending' });
    const target = before[0]!;
    try {
      await reportPurchased(
        client,
        target.id,
        {
          quantityPurchased: target.quantityRequested,
          actualPaidOriginal: { amountMinor: 100_000, currency: 'JPY' },
          actualPaidBooking: { amountMinor: 2_000_0, currency: 'TWD' },
        },
        { idempotencyKey: 'procurement-smoke-4' },
      );
      expect.unreachable('應該要 422');
    } catch (cause) {
      expect(cause).toBeInstanceOf(ApiError);
      const apiError = cause as ApiError;
      expect(apiError.problem.detail).toBe('這個品項已經被標記為缺貨，不能再回報買到，請重新整理清單。');
    }
  });

  it('對話框用的 payload-aware 冪等鍵：同一份回報內容重送用同一把 key，改了金額才換新的', () => {
    // ReportPurchasedDialog 送出時的 payload 形狀（見 page.tsx handleReportConfirm）。
    const scope = createPayloadIdempotencyScope();
    const payload = {
      purchaseItemId: PURCHASE_ITEM_IDS.pendingMask,
      input: {
        quantityPurchased: 2,
        actualPaidOriginal: { amountMinor: 240_000, currency: 'JPY' },
        actualPaidBooking: { amountMinor: 54_000, currency: 'TWD' },
      },
    };
    const firstKey = scope.current(payload);
    const retryKey = scope.current(payload);
    expect(retryKey).toBe(firstKey);

    const changedPayload = { ...payload, input: { ...payload.input, quantityPurchased: 3 } };
    const changedKey = scope.current(changedPayload);
    expect(changedKey).not.toBe(firstKey);
  });

  it('找不到的採購項目回 404', async () => {
    await expect(
      reportPurchased(
        client,
        'not-a-real-id',
        {
          quantityPurchased: 1,
          actualPaidOriginal: { amountMinor: 100_000, currency: 'JPY' },
          actualPaidBooking: { amountMinor: 2_000_0, currency: 'TWD' },
        },
        { idempotencyKey: 'procurement-smoke-5' },
      ),
    ).rejects.toThrow();
  });
});
