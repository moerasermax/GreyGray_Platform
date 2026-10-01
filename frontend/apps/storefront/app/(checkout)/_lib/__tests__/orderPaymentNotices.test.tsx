import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import type { components } from '@greygray/api-client/storefront';
import { describe, expect, it } from 'vitest';
import {
  CancellationNotice,
  PAYMENT_OVERDUE_MESSAGE,
  PaymentOverdueNotice,
  cancellationExplanation,
} from '../orderPaymentNotices';
import { PAYMENT_OVERDUE_TITLE } from '../paymentInstructions';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];
const paidAmount = { amountMinor: 123400, currency: 'TWD' } as const;

function cancellation(
  cancellationSource: S['OrderCancellationSource'] | null,
  paid: S['Money'] | null = null,
  cancelledAt: string | null = null,
) {
  return { cancellationSource, paidAmount: paid, cancelledAt };
}

describe('FE-61 T4：取消說明', () => {
  it('依來源與是否已付款使用五種固定主句', () => {
    expect(cancellationExplanation(cancellation('PaymentExpired'))[0]).toBe('這筆訂單因逾期未付款，已由系統自動取消。');
    expect(cancellationExplanation(cancellation('PaymentExpired', paidAmount))[0]).toBe('這筆訂單已由系統自動取消。');
    expect(cancellationExplanation(cancellation('Staff'))[0]).toBe('這筆訂單已由客服取消。');
    expect(cancellationExplanation(cancellation('Customer'))[0]).toBe('你已取消這筆訂單。');
    expect(cancellationExplanation(cancellation(null))[0]).toBe('這筆訂單已取消。');
    expect(cancellationExplanation({ ...cancellation(null), cancellationSource: 'FutureSource' as S['OrderCancellationSource'] })[0])
      .toBe('這筆訂單已取消。');
  });

  it('退款句只在 amountMinor 大於 0 時出現，客服句最多一次', () => {
    const unpaid = cancellationExplanation(cancellation('Customer'));
    const zero = cancellationExplanation(cancellation('Staff', { amountMinor: 0, currency: 'TWD' }));
    const paidStaff = cancellationExplanation(cancellation('Staff', paidAmount));

    expect(unpaid).not.toContain('已付款項會由我們辦理退款。');
    expect(zero).not.toContain('已付款項會由我們辦理退款。');
    expect(paidStaff).toContain('已付款項會由我們辦理退款。');
    expect(paidStaff.filter((line) => line === '如有疑問請聯絡客服。')).toHaveLength(1);
  });

  it('固定 UTC 取消時間轉台北格式，且元件不接受或顯示後端原因文字', () => {
    const order = cancellation('Customer', null, '2026-10-01T16:30:00Z');
    const html = renderToStaticMarkup(<CancellationNotice order={order} />);
    expect(html).toContain('取消時間：2026/10/02 00:30（台灣時間）');
    expect(html).not.toContain('後台秘密原因');
  });
});

describe('FE-61 T5：確認中區塊', () => {
  it('文案一字不差且沒有付款按鈕', () => {
    const html = renderToStaticMarkup(<PaymentOverdueNotice />);
    expect(html).toContain(PAYMENT_OVERDUE_TITLE);
    expect(html).toContain(PAYMENT_OVERDUE_MESSAGE);
    expect(html).not.toMatch(/<button\b/);
    expect(html).not.toContain('前往付款');
  });
});
