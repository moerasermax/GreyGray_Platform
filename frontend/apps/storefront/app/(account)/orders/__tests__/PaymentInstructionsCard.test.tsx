import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import type { components } from '@greygray/api-client/storefront';
import { describe, expect, it } from 'vitest';
import { PaymentInstructionsCard } from '../_components/PaymentInstructionsCard';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];

const expiresAt = '2026-10-04T15:59:59Z';
const grandTotal = { amountMinor: 123400, currency: 'TWD' } as const;

function renderInstructions(instructions: S['PaymentInstructions']): string {
  return renderToStaticMarkup(<PaymentInstructionsCard order={{ grandTotal, paymentInstructions: instructions }} />);
}

function instructions(
  method: S['PaymentMethod'],
  fields: Partial<S['PaymentInstructions']>,
): S['PaymentInstructions'] {
  return { method, expiresAt, issuedAt: '2026-10-01T00:00:00Z', ...fields };
}

describe('FE-57 T3：取號卡', () => {
  it('ATM 顯示完整欄位、後端含運總額、共用提示與 44px 複製鈕', () => {
    const html = renderInstructions(
      instructions('Atm', { bankCode: '822', virtualAccount: '9912345678901234' }),
    );

    for (const text of [
      '請在期限內完成 ATM 轉帳',
      '銀行代碼',
      '822',
      '轉帳帳號',
      '9912345678901234',
      '應繳金額',
      'NT$1,234',
      '繳費期限',
      '2026/10/04 23:59（台灣時間）',
      '繳費完成後，系統收到通知會自動更新訂單，可能需要一些時間。',
      '逾期未繳，訂單會自動取消。',
      '這筆訂單已產生繳費資訊，請不要重新付款。',
    ]) {
      expect(html).toContain(text);
    }
    expect(html).toContain('aria-label="複製轉帳帳號"');
    expect(html).toContain('min-h-[var(--gg-touch-min)]');
    expect(html).toMatch(/<p[^>]*aria-live="polite"/);
    expect(html).not.toMatch(/<button[^>]*aria-live=/);
  });

  it('超商代碼顯示機台說明、代碼與可複製按鈕', () => {
    const html = renderInstructions(instructions('ConvenienceStoreCode', { paymentNo: 'CVS123456' }));
    expect(html).toContain('請到超商多媒體機台輸入繳費代碼，列印繳費單後到櫃檯繳費');
    expect(html).toContain('CVS123456');
    expect(html).toContain('aria-label="複製繳費代碼"');
  });

  it('條碼只顯示三段文字且順序不變，不產生條碼圖片元素', () => {
    const html = renderInstructions(instructions('Barcode', { barcodes: ['BAR-1', 'BAR-2', 'BAR-3'] }));
    expect(html).toContain('請在超商櫃檯出示以下三段條碼繳費');
    const positions = ['BAR-1', 'BAR-2', 'BAR-3'].map((barcode) => html.indexOf(barcode));
    expect(positions.every((position) => position >= 0)).toBe(true);
    expect(positions).toEqual([...positions].sort((left, right) => left - right));
    expect(html).not.toMatch(/<(img|svg|canvas)\b/);
  });
});
