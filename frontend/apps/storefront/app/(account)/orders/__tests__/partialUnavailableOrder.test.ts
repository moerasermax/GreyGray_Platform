import { orders } from '@greygray/api-client/mock/fixtures.storefront';
import { describe, expect, it } from 'vitest';

/**
 * FE-15：客人要看得到「部分品項缺貨」而不是整張單被取消。
 * 這條測試守住 fixture 的形狀，不是重算退款邏輯——退款金額本來就是後端回的。
 */
describe('部分品項缺貨的訂單 fixture', () => {
  const order = orders.find((o) => o.lines.some((line) => line.status === 'Unavailable'));

  it('至少要有一張訂單含 Unavailable line，這是這一包的主要測試資料', () => {
    expect(order).toBeDefined();
  });

  it('訂單本身不是 Cancelled——缺貨只取消該 line，不是整張作廢', () => {
    expect(order?.status).not.toBe('Cancelled');
  });

  it('Unavailable 的 line 帶有 refundedAmount', () => {
    const unavailableLine = order?.lines.find((line) => line.status === 'Unavailable');
    expect(unavailableLine?.refundedAmount?.amountMinor).toBeGreaterThan(0);
  });

  it('其餘 line 維持正常狀態，照常出貨——不會被 Unavailable 波及', () => {
    const otherLines = order?.lines.filter((line) => line.status !== 'Unavailable') ?? [];
    expect(otherLines.length).toBeGreaterThan(0);
    for (const line of otherLines) {
      expect(line.refundedAmount).toBeNull();
    }
  });
});
