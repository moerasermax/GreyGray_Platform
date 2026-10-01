import { describe, expect, it } from 'vitest';
import { orders } from '../fixtures.storefront';

describe('FE-57 mock 取號訂單', () => {
  it('新種子接在最後，ATM 期限與訂單付款期限完全相同', () => {
    const order = orders.at(-1);
    expect(order?.status).toBe('AwaitingPayment');
    expect(order?.paymentInstructions?.method).toBe('Atm');
    expect(order?.paymentInstructions?.bankCode).toBe('822');
    expect(order?.paymentInstructions?.virtualAccount).toBe('9912345678901234');
    expect(order?.paymentDueAt).toBe(order?.paymentInstructions?.expiresAt);
  });
});
