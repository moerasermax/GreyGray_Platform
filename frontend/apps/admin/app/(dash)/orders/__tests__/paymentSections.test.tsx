import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import { ManualRefundDialog } from '../_components/ManualRefundDialog';
import { ManualRefundSection } from '../_components/ManualRefundSection';
import { PaymentInstructions } from '../_components/PaymentInstructions';

(globalThis as unknown as { React: typeof React }).React = React;
type S = components['schemas'];

const instructions: S['PaymentInstructions'] = {
  method: 'Atm', bankCode: '822', virtualAccount: '0099887766',
  expiresAt: '2026-10-04T15:59:59Z', issuedAt: '2026-10-01T02:00:00Z',
};

const refund: S['ManualRefund'] = {
  status: 'Pending',
  requiredAmount: { amountMinor: 90_000, currency: 'TWD' },
  recordedAmount: { amountMinor: 12_300, currency: 'TWD' },
  outstandingAmount: { amountMinor: 45_600, currency: 'TWD' },
  entries: [{
    id: '0199f0c2-0000-7000-8000-0000000000a1',
    amount: { amountMinor: 12_300, currency: 'TWD' },
    remittedOn: '2026-10-01', note: '後五碼 12345',
    recordedBy: '0199f0c2-0000-7000-8000-0000000000a2', recordedByName: '陳店長', recordedAt: '2026-10-01T02:00:00Z',
  }, {
    id: '0199f0c2-0000-7000-8000-0000000000a3',
    amount: { amountMinor: 100, currency: 'TWD' },
    remittedOn: '2026-10-02', note: '第二筆',
    recordedBy: '0199f0c2-0000-7000-8000-0000000000a4', recordedByName: '王會計', recordedAt: '2026-10-02T02:00:00Z',
  }],
};

describe('取號資訊', () => {
  it('ATM 欄位正確，Captured 仍顯示並標已繳費', () => {
    const html = renderToStaticMarkup(<PaymentInstructions instructions={instructions} paymentStatus="Captured" orderStatus="PaidAwaitingClose" />);
    expect(html).toContain('銀行代碼');
    expect(html).toContain('0099887766');
    expect(html).toContain('已繳費');
  });
  it('取消訂單仍停 InstructionsIssued 時標失效', () => {
    const html = renderToStaticMarkup(<PaymentInstructions instructions={instructions} paymentStatus="InstructionsIssued" orderStatus="Cancelled" />);
    expect(html).toContain('已失效，請勿提供給客人');
  });
  it('instructions null 不顯示也不丟例外', () => {
    expect(renderToStaticMarkup(<PaymentInstructions instructions={null} paymentStatus="Pending" orderStatus="AwaitingPayment" />)).toBe('');
  });
});

describe('人工退款區塊', () => {
  it('三個不一致的後端金額逐一顯示，不自行相減；有權限顯示按鈕', () => {
    const html = renderToStaticMarkup(<ManualRefundSection paymentId="p1" manualRefund={refund} canRecord onRecord={() => {}} />);
    expect(html).toContain('NT$900');
    expect(html).toContain('NT$123');
    expect(html).toContain('NT$456');
    expect(html).toContain('2026/10/01');
    expect(html).toContain('登記已匯款');
    expect(html.indexOf('陳店長')).toBeLessThan(html.indexOf('王會計'));
  });
  it('Completed 沒有表單按鈕，並說明之後可能再出現', () => {
    const html = renderToStaticMarkup(<ManualRefundSection paymentId="p1" manualRefund={{ ...refund, status: 'Completed' }} canRecord onRecord={() => {}} />);
    expect(html).not.toContain('登記已匯款');
    expect(html).toContain('之後若有新的退款要求');
  });
  it('manualRefund null 不顯示', () => {
    expect(renderToStaticMarkup(<ManualRefundSection paymentId="p1" manualRefund={null} canRecord onRecord={() => {}} />)).toBe('');
  });
  it('submitting 時確認鈕 disabled，且銀行帳號警語永遠存在', () => {
    const html = renderToStaticMarkup(<ManualRefundDialog open amountInput="500" remittedOn="2026-10-01" noteInput="" today="2026-10-01" error={null} submitting onAmountChange={() => {}} onRemittedOnChange={() => {}} onNoteChange={() => {}} onClose={() => {}} onSubmit={() => {}} />);
    expect(html).toContain('disabled');
    expect(html).toContain('請勿填寫客人完整銀行帳號');
  });
});
