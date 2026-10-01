import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../../http';
import * as api from '../../endpoints/storefront';
import { orders } from '../fixtures.storefront';
import { STOREFRONT_BASE_URL, resetStorefrontMockState } from '../handlers.storefront';
import { storefrontServer } from '../server';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });
const options = { idempotencyKey: 'fe-61-payment-overdue' };

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  vi.restoreAllMocks();
  storefrontServer.resetHandlers();
  resetStorefrontMockState();
});
afterAll(() => storefrontServer.close());

describe('FE-61 T7：mock 逾期與取消', () => {
  it('四筆新種子依序是逾期、系統逾期取消、客服取消已付款、客人取消', () => {
    const [overdue, expired, staff, customer] = orders.slice(-4);
    expect(overdue).toMatchObject({ status: 'AwaitingPayment', paymentOverdue: true, paymentInstructions: null });
    expect(Date.parse(overdue!.paymentDueAt!)).toBeLessThan(Date.now());
    expect(expired).toMatchObject({ status: 'Cancelled', cancellationSource: 'PaymentExpired', paidAmount: null });
    expect(staff).toMatchObject({ status: 'Cancelled', cancellationSource: 'Staff' });
    expect(staff!.paidAmount!.amountMinor).toBeGreaterThan(0);
    expect(customer).toMatchObject({ status: 'Cancelled', cancellationSource: 'Customer', paidAmount: null });
  });

  it('逾期訂單發動付款回 422 ordering.payment-overdue', async () => {
    const overdue = orders.find((order) => order.paymentOverdue === true);
    expect(overdue).toBeDefined();
    await expect(api.initiatePayment(client, overdue!.id, options)).rejects.toMatchObject({
      status: 422,
      code: 'ordering.payment-overdue',
    });
  });

  it('GET 依目前時間動態算逾期，缺席值視為 false、剛好到期即為 true', async () => {
    const candidate = orders.find((order) =>
      order.status === 'AwaitingPayment'
      && order.paymentOverdue === undefined
      && order.paymentInstructions == null
      && order.paymentDueAt != null);
    expect(candidate).toBeDefined();

    vi.spyOn(Date, 'now').mockReturnValue(Date.parse(candidate!.paymentDueAt!));
    const refreshed = await api.getOrder(client, candidate!.id);
    expect(refreshed.paymentOverdue).toBe(true);
  });

  it('種子明寫的 paymentOverdue 優先於動態計算', async () => {
    const atm = orders.find((order) => order.paymentInstructions?.method === 'Atm');
    expect(atm).toBeDefined();
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse(atm!.paymentDueAt!) + 1);
    const refreshed = await api.getOrder(client, atm!.id);
    expect(refreshed.paymentOverdue).toBe(false);
  });

  it('自助取消不可變地補來源與時間並清掉取號資訊，reset 可深層還原', async () => {
    const atm = orders.find((order) => order.paymentInstructions?.method === 'Atm');
    expect(atm).toBeDefined();

    const cancelled = await api.cancelOrder(client, atm!.id, {}, options);
    expect(cancelled).toMatchObject({
      status: 'Cancelled',
      cancellationSource: 'Customer',
      paymentOverdue: false,
      paymentInstructions: null,
    });
    expect(cancelled.cancelledAt).toBeTruthy();
    expect(atm!.paymentInstructions?.method).toBe('Atm');

    resetStorefrontMockState();
    const restored = await api.getOrder(client, atm!.id);
    expect(restored.paymentInstructions?.method).toBe('Atm');
    expect(restored.cancellationSource).toBeNull();
  });
});
