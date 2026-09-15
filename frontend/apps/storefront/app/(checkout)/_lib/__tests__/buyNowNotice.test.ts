import { describe, expect, it } from 'vitest';
import { shouldShowBuyNowNotice } from '../buyNowNotice';

describe('shouldShowBuyNowNotice', () => {
  it('?from=buy-now 會顯示', () => {
    expect(shouldShowBuyNowNotice('?from=buy-now')).toBe(true);
  });

  it.each(['', '?q=mask', '?from=home'])('%o 不顯示', (search) => {
    expect(shouldShowBuyNowNotice(search)).toBe(false);
  });

  it('有其他參數時仍正確判斷', () => {
    expect(shouldShowBuyNowNotice('?campaign=seoul&from=buy-now&quantity=2')).toBe(true);
  });

  it('重複的 from 裡有 buy-now 仍顯示', () => {
    expect(shouldShowBuyNowNotice('?from=home&from=buy-now')).toBe(true);
  });
});
