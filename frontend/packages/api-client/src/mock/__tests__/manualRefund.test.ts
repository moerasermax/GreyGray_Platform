import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { ApiClient } from '../../http';
import * as api from '../../endpoints/admin';
import { ADMIN_BASE_URL, resetAdminMockState } from '../handlers.admin';
import { adminServer } from '../server';

const client = new ApiClient({ baseUrl: ADMIN_BASE_URL });
let key = 0;
const options = () => ({ idempotencyKey: `manual-refund-${++key}` });

beforeAll(() => adminServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  adminServer.resetHandlers();
  resetAdminMockState();
});
afterAll(() => adminServer.close());

function taipeiDate(offsetDays = 0): string {
  const date = new Date(Date.now() + offsetDays * 86_400_000);
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Taipei', year: 'numeric', month: '2-digit', day: '2-digit' })
      .formatToParts(date)
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  );
  return `${parts.year}-${parts.month}-${parts.day}`;
}

async function orderByNumber(orderNumber: string) {
  const page = await api.listOrders(client, { q: orderNumber, limit: 100 });
  const item = page.items.find((order) => order.orderNumber === orderNumber);
  if (!item) throw new Error(`找不到 mock 訂單 ${orderNumber}`);
  return api.getOrder(client, item.id);
}

describe('人工退款 mock handler', () => {
  it('可分次登記，部分後仍 Pending，補足後 Completed', async () => {
    const seeded = await orderByNumber('GG26100100059');
    const payment = seeded.payments?.[0];
    expect(payment?.manualRefund?.entries).toHaveLength(1);

    const partial = await api.recordManualRefund(client, seeded.id, payment!.id, {
      amount: { amountMinor: 20_000, currency: 'TWD' }, remittedOn: taipeiDate(), note: '第二次',
    }, options());
    expect(partial.payments?.[0]?.manualRefund).toMatchObject({
      status: 'Pending', recordedAmount: { amountMinor: 50_000 }, outstandingAmount: { amountMinor: 50_000 },
    });
    expect(partial.payments?.[0]?.manualRefund?.entries).toHaveLength(2);

    const completed = await api.recordManualRefund(client, seeded.id, payment!.id, {
      amount: { amountMinor: 50_000, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options());
    expect(completed.payments?.[0]?.manualRefund).toMatchObject({
      status: 'Completed', recordedAmount: { amountMinor: 100_000 }, outstandingAmount: { amountMinor: 0 },
    });
    expect(completed.payments?.[0]?.manualRefund?.entries).toHaveLength(3);
  });

  it('超額回 exceeds-outstanding', async () => {
    const order = await orderByNumber('GG26100100059');
    await expect(api.recordManualRefund(client, order.id, order.payments![0]!.id, {
      amount: { amountMinor: 80_000, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options())).rejects.toMatchObject({ problem: { status: 422, code: 'payment.manual-refund-exceeds-outstanding' } });
  });

  it('manualRefund null 與 Completed 都回 not-required', async () => {
    const noRefund = await orderByNumber('GG26082800031');
    await expect(api.recordManualRefund(client, noRefund.id, noRefund.payments![0]!.id, {
      amount: { amountMinor: 100, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options())).rejects.toMatchObject({ problem: { code: 'payment.manual-refund-not-required' } });

    const completed = await orderByNumber('GG26100100060');
    await expect(api.recordManualRefund(client, completed.id, completed.payments![0]!.id, {
      amount: { amountMinor: 100, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options())).rejects.toMatchObject({ problem: { code: 'payment.manual-refund-not-required' } });
  });

  it('小數元與台北明天分別回 amount-invalid、date-invalid', async () => {
    const order = await orderByNumber('GG26100100059');
    const paymentId = order.payments![0]!.id;
    await expect(api.recordManualRefund(client, order.id, paymentId, {
      amount: { amountMinor: 1, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options())).rejects.toMatchObject({ problem: { code: 'payment.manual-refund-amount-invalid' } });
    await expect(api.recordManualRefund(client, order.id, paymentId, {
      amount: { amountMinor: 100, currency: 'TWD' }, remittedOn: taipeiDate(1), note: null,
    }, options())).rejects.toMatchObject({ problem: { code: 'payment.manual-refund-date-invalid' } });
  });

  it('找不到訂單或付款先回 platform.not-found', async () => {
    const order = await orderByNumber('GG26100100059');
    await expect(api.recordManualRefund(client, order.id, '00000000-0000-0000-0000-000000000000', {
      amount: { amountMinor: 100, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options())).rejects.toMatchObject({ problem: { status: 404, code: 'platform.not-found' } });
  });

  it('reset 後 entries 回到種子的一筆，不受先前不可變更新污染', async () => {
    const order = await orderByNumber('GG26100100059');
    await api.recordManualRefund(client, order.id, order.payments![0]!.id, {
      amount: { amountMinor: 20_000, currency: 'TWD' }, remittedOn: taipeiDate(), note: null,
    }, options());
    resetAdminMockState();
    const reset = await api.getOrder(client, order.id);
    expect(reset.payments?.[0]?.manualRefund?.entries).toHaveLength(1);
    expect(reset.payments?.[0]?.manualRefund?.recordedAmount.amountMinor).toBe(30_000);
  });
});
