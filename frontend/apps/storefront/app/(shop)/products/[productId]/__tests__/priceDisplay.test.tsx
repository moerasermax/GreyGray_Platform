/**
 * 商品詳情頁的價格顯示（#40／ADR-033）：底部列「N 件 · 小計」與資訊區「單價」。
 *
 * 沒有 jsdom，所以用 `renderToStaticMarkup`（做法與理由見
 * `apps/admin/app/(dash)/__tests__/dashboardLedger.test.tsx` 檔頭）。
 * `AddToCartPanel` 的 `useState` 變化測不到，但這兩個元件是純呈現的——
 * 畫什麼完全由 props 決定，所以「數量 5、單價 NT$60 → NT$300」正好驗得到。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { Money } from '@greygray/api-client';
import { BottomBarSummary } from '../_components/BottomBarSummary';
import { UnitPriceBlock } from '../_components/UnitPriceBlock';

(globalThis as unknown as { React: typeof React }).React = React;

const NT60: Money = { amountMinor: 6000, currency: 'TWD' };

// 這兩句的字面由 `AddToCartPanel` 決定（`noPriceMessage`），元件只負責畫。
const PREORDER_NO_PRICE = '售價在開團時決定，開團後才能加入購物車。';
const STOCK_NO_PRICE = '這個規格尚未定價，請稍後再試。';

describe('BottomBarSummary：底部列的「N 件 · 小計」', () => {
  it('數量 5 ＋ 單價 NT$60 → 畫出「5 件」與小計 NT$300（使用者回報的那一組）', () => {
    const html = renderToStaticMarkup(
      <BottomBarSummary quantity={5} price={NT60} unitPriceLabel={null} />,
    );
    expect(html).toContain('5 件');
    expect(html).toContain('小計');
    expect(html).toContain('NT$300');
  });

  it('數量 1 時小計等於單價，畫面上仍看得到「1 件」', () => {
    const html = renderToStaticMarkup(
      <BottomBarSummary quantity={1} price={NT60} unitPriceLabel={null} />,
    );
    expect(html).toContain('1 件');
    expect(html).toContain('NT$60');
  });

  it('有包裝單價時第一行用它，不重複組字串', () => {
    const html = renderToStaticMarkup(
      <BottomBarSummary quantity={2} price={NT60} unitPriceLabel="NT$60／32 顆" />,
    );
    expect(html).toContain('NT$60／32 顆');
    expect(html).toContain('NT$120');
  });

  it('沒有價格時整塊留白，不出現任何金額字樣（理由由上方的 disabledReason 說）', () => {
    const html = renderToStaticMarkup(
      <BottomBarSummary quantity={3} price={null} unitPriceLabel={null} />,
    );
    expect(html).not.toContain('NT$');
    expect(html).not.toContain('小計');
    expect(html).not.toContain('件');
  });
});

describe('UnitPriceBlock：商品資訊區的「單價」', () => {
  it('有價格就顯示單價', () => {
    const html = renderToStaticMarkup(
      <UnitPriceBlock price={NT60} unitPriceLabel={null} variantLabel={null} noPriceMessage={STOCK_NO_PRICE} />,
    );
    expect(html).toContain('單價');
    expect(html).toContain('NT$60');
  });

  it('多規格時一併顯示已選規格名與包裝單價', () => {
    const html = renderToStaticMarkup(
      <UnitPriceBlock
        price={NT60}
        unitPriceLabel="NT$60／32 顆"
        variantLabel="30 入"
        noPriceMessage={STOCK_NO_PRICE}
      />,
    );
    expect(html).toContain('30 入');
    expect(html).toContain('NT$60／32 顆');
  });

  it('預購沒有價格 → 資訊區說「售價在開團時決定，開團後才能加入購物車。」，而且不出現金額', () => {
    const html = renderToStaticMarkup(
      <UnitPriceBlock price={null} unitPriceLabel={null} variantLabel={null} noPriceMessage={PREORDER_NO_PRICE} />,
    );
    expect(html).toContain('售價在開團時決定，開團後才能加入購物車。');
    expect(html).not.toContain('NT$');
  });

  it('現貨沒有價格 → 資訊區說「這個規格尚未定價，請稍後再試。」，而且不出現金額', () => {
    const html = renderToStaticMarkup(
      <UnitPriceBlock price={null} unitPriceLabel={null} variantLabel={null} noPriceMessage={STOCK_NO_PRICE} />,
    );
    expect(html).toContain('這個規格尚未定價，請稍後再試。');
    expect(html).not.toContain('NT$');
  });

  it('文案完全由 props 決定，元件裡不留任何寫死的說法', () => {
    const html = renderToStaticMarkup(
      <UnitPriceBlock price={null} unitPriceLabel={null} variantLabel={null} noPriceMessage="任意一句" />,
    );
    expect(html).toContain('任意一句');
    expect(html).not.toContain('尚未定價');
    expect(html).not.toContain('開團');
  });
});

describe('沒有價格時，說明只出現在資訊區，底部列一個金額都不畫', () => {
  it.each([
    [PREORDER_NO_PRICE, '預購'],
    [STOCK_NO_PRICE, '現貨'],
  ])('%s（%s）', (noPriceMessage) => {
    const info = renderToStaticMarkup(
      <UnitPriceBlock price={null} unitPriceLabel={null} variantLabel={null} noPriceMessage={noPriceMessage} />,
    );
    const bar = renderToStaticMarkup(
      <BottomBarSummary quantity={5} price={null} unitPriceLabel={null} />,
    );
    expect(info).toContain(noPriceMessage);
    expect(bar).not.toContain('NT$');
    expect(bar).not.toContain('小計');
    expect(bar).not.toContain(noPriceMessage);
  });
});
