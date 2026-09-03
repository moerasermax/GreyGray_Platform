/**
 * 商品頁 SKU 區的畫面（`_components/SkuSection.tsx`）。
 *
 * 沒有 jsdom，所以用 `renderToStaticMarkup`（做法與理由見
 * `(dash)/__tests__/dashboardLedger.test.tsx` 檔頭）。effect 不會跑，
 * 但這個元件是純呈現的——它畫什麼完全由 props 決定，正好驗得到。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import { SkuSection } from '../_components/SkuSection';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];

const SKU: S['AdminSku'] = {
  id: 'aaaaaaaabbbbbbbbccccccccdddddddd',
  available: 12,
  name: '森田藥粧 玻尿酸保濕面膜',
  variantName: '30 入',
  weightGram: 120,
  size: { lengthCm: 8, widthCm: 6, heightCm: 4 },
  unitOfMeasure: null,
  unitCount: null,
  listPrice: { amountMinor: 39900, currency: 'TWD' },
  isActive: true,
};

function render(canWrite: boolean, skus: readonly S['AdminSku'][] = [SKU]): string {
  return renderToStaticMarkup(
    <SkuSection
      mode="Stock"
      skus={skus}
      loading={false}
      canWrite={canWrite}
      onAdd={() => {}}
      onEdit={() => {}}
      onReceive={() => {}}
    />,
  );
}

describe('SKU 區：寫入權決定看不看得到按鈕', () => {
  it('有寫入權：標題列有「新增 SKU」，每一列有「進貨」', () => {
    const html = render(true);

    expect(html).toContain('新增 SKU');
    expect(html).toContain('進貨');
  });

  it('★ 沒有寫入權：新增與進貨兩個按鈕都不畫出來', () => {
    const html = render(false);

    expect(html).not.toContain('新增 SKU');
    expect(html).not.toContain('進貨');
    // 一個 <button> 都沒有——列也不可點（DataTable 沒有 onRowClick）。
    expect(html).not.toContain('<button');
  });

  it('空狀態文案：有寫入權指向右上角的按鈕，沒有寫入權說要什麼權限', () => {
    expect(render(true, [])).toContain('點右上角「新增 SKU」');
    expect(render(false, [])).toContain('Operator');
    // 舊文案（「契約目前只有 PATCH…」）已經不成立，ADR-032 之後不准再出現。
    expect(render(true, [])).not.toContain('契約目前只有 PATCH');
    expect(render(false, [])).not.toContain('契約目前只有 PATCH');
  });

  it('可用量與標價逐字來自傳進來的 SKU，畫面上不做任何運算', () => {
    const html = render(true);

    expect(html).toContain('>12<');
    expect(html).toContain('NT$399');
    expect(html).toContain('120 g');
  });
});
