/**
 * 出貨單列表「訂單」欄（#48）：使用者 2026-09-04 因為看不出掛的是哪一張訂單，
 * 把交運按錯了單。沒有 jsdom，用 `renderToStaticMarkup` 把純呈現元件畫成 HTML
 * 字串再斷言（做法與理由見 `../../__tests__/dashboardLedger.test.tsx` 檔頭）。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ShipmentOrderLinksCell } from '../_components/ShipmentOrderLinksCell';

(globalThis as unknown as { React: typeof React }).React = React;

const ORDER_A = '0199f0c2-0000-7000-8000-0000000000a1';
const ORDER_B = '0199f0c2-0000-7000-8000-0000000000a2';

function renderCell(props: Partial<React.ComponentProps<typeof ShipmentOrderLinksCell>> = {}): string {
  return renderToStaticMarkup(
    <table>
      <tbody>
        <tr>
          <ShipmentOrderLinksCell
            orderIds={[ORDER_A]}
            orderNumberByOrderId={new Map()}
            ordersLoadFailed={false}
            {...props}
          />
        </tr>
      </tbody>
    </table>,
  );
}

describe('出貨單列表「訂單」欄：#48 使用者按錯單之後補的那一欄', () => {
  it('查得到訂單編號時顯示編號並連到訂單頁', () => {
    const html = renderCell({ orderNumberByOrderId: new Map([[ORDER_A, 'GG26082800031']]) });

    expect(html).toContain('GG26082800031');
    expect(html).toContain(`/orders/${ORDER_A}`);
  });

  it('★ map 查不到就退回前 8 碼 id，不是空白——查不到跟沒有掛訂單是兩件事', () => {
    const html = renderCell({ orderNumberByOrderId: new Map() });

    expect(html).toContain(ORDER_A.slice(0, 8));
    expect(html).not.toContain('—');
  });

  it('★ 一張出貨單掛多張訂單：全部列出並講清楚是合併出貨', () => {
    const html = renderCell({
      orderIds: [ORDER_A, ORDER_B],
      orderNumberByOrderId: new Map([
        [ORDER_A, 'GG26082800031'],
        [ORDER_B, 'GG260828022'],
      ]),
    });

    expect(html).toContain('GG26082800031');
    expect(html).toContain('GG260828022');
    expect(html).toContain('合併出貨');
    expect(html).toContain('共 2 張');
  });

  it('合併出貨時其中一張查不到編號，仍然個別退回前 8 碼，不會讓另一張也跟著查不到', () => {
    const html = renderCell({
      orderIds: [ORDER_A, ORDER_B],
      orderNumberByOrderId: new Map([[ORDER_A, 'GG26082800031']]),
    });

    expect(html).toContain('GG26082800031');
    expect(html).toContain(ORDER_B.slice(0, 8));
  });

  it('★ 訂單清單整個讀取失敗時退回原本的張數顯示，不逐一顯示 id', () => {
    const html = renderCell({ orderIds: [ORDER_A, ORDER_B], ordersLoadFailed: true });

    expect(html).toContain('2 張');
    expect(html).toContain('合併出貨');
    expect(html).not.toContain(ORDER_A.slice(0, 8));
    expect(html).not.toContain(`/orders/${ORDER_A}`);
  });

  it('單張訂單、清單讀取失敗時不講「合併出貨」', () => {
    const html = renderCell({ ordersLoadFailed: true });

    expect(html).toContain('1 張');
    expect(html).not.toContain('合併出貨');
  });
});
