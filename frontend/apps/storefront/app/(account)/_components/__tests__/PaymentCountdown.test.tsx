import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import {
  PaymentCountdown,
  formatRemaining,
  isPaymentCountdownExpiring,
} from '../PaymentCountdown';

(globalThis as unknown as { React: typeof React }).React = React;

describe('FE-57 T7：付款倒數', () => {
  it('3 天範圍、剛好 24 小時與 24 小時內使用指定格式', () => {
    expect(formatRemaining(3 * 86_400_000 - 1_000)).toBe('2 天 23:59:59');
    expect(formatRemaining(86_400_000)).toBe('1 天 00:00:00');
    expect(formatRemaining(86_400_000 - 1_000)).toBe('23:59:59');
    expect(formatRemaining(0)).toBe('00:00:00');
  });

  it('剩 5 分鐘內變紅的邊界不變', () => {
    expect(isPaymentCountdownExpiring(5 * 60_000 + 1)).toBe(false);
    expect(isPaymentCountdownExpiring(5 * 60_000)).toBe(true);
    expect(isPaymentCountdownExpiring(1)).toBe(true);
    expect(isPaymentCountdownExpiring(0)).toBe(false);
  });

  it('元件在倒數下方顯示台北絕對期限', () => {
    vi.spyOn(Date, 'now').mockReturnValue(new Date('2026-10-01T15:59:59Z').getTime());
    const html = renderToStaticMarkup(
      <PaymentCountdown paymentDueAt="2026-10-04T15:59:59Z" onExpire={() => {}} />,
    );
    expect(html).toContain('3 天 00:00:00');
    expect(html).toContain('繳費期限：2026/10/04 23:59（台灣時間）');
    vi.restoreAllMocks();
  });
});
