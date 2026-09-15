import type { components } from '@greygray/api-client/storefront';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it, vi } from 'vitest';
import { buyNowDestination, executeCartIntent, type CartIntent } from '../_lib/buyNow';

type Cart = components['schemas']['Cart'];

function cart(lines: Array<{ skuId: string; quantity: number }>): Cart {
  return {
    id: 'cart-1',
    lines: lines.map((line, index) => ({
      id: `line-${index}`,
      skuId: line.skuId,
      productId: `product-${index}`,
      name: `商品 ${index}`,
      mode: 'Stock',
      quantity: line.quantity,
      unitPrice: { amountMinor: 10000, currency: 'TWD' },
      lineTotal: { amountMinor: 10000, currency: 'TWD' },
    })),
    goodsTotal: { amountMinor: 10000, currency: 'TWD' },
    hasMixedModes: false,
  };
}

describe('buyNowDestination', () => {
  it('空車加入一件後只剩同 SKU、同數量的一行，直接去結帳', () => {
    expect(buyNowDestination(cart([{ skuId: 'sku-a', quantity: 2 }]), { skuId: 'sku-a', quantity: 2 }))
      .toBe('/checkout');
  });

  it('購物車還有別的品項，先去購物車確認', () => {
    expect(buyNowDestination(cart([
      { skuId: 'sku-a', quantity: 2 },
      { skuId: 'sku-b', quantity: 1 },
    ]), { skuId: 'sku-a', quantity: 2 })).toBe('/cart?from=buy-now');
  });

  it('同 SKU 併進舊行後數量比這次選的多，先去購物車確認', () => {
    expect(buyNowDestination(cart([{ skuId: 'sku-a', quantity: 5 }]), { skuId: 'sku-a', quantity: 2 }))
      .toBe('/cart?from=buy-now');
  });
});

describe('executeCartIntent', () => {
  it.each([
    ['add-to-cart', 'buy-now'],
    ['buy-now', 'add-to-cart'],
  ] as const)('同步搶按 %s 再 %s：只送一次，第一個意圖勝出', async (firstIntent, secondIntent) => {
    let resolve!: (value: Cart) => void;
    const request = new Promise<Cart>((done) => { resolve = done; });
    const addLine = vi.fn(() => request);
    const added = vi.fn();
    const navigate = vi.fn();
    const pendingRef = { current: false };
    const run = (intent: CartIntent) => executeCartIntent({
      intent,
      selection: { skuId: 'sku-a', quantity: 1 },
      pendingRef,
      addLine,
      onStart: vi.fn(),
      onCartUpdated: vi.fn(),
      onAdded: added,
      onNavigate: navigate,
      onError: vi.fn(),
    });

    const first = run(firstIntent);
    const second = run(secondIntent);
    expect(addLine).toHaveBeenCalledTimes(1);
    resolve(cart([{ skuId: 'sku-a', quantity: 1 }]));
    await Promise.all([first, second]);

    expect(added).toHaveBeenCalledTimes(firstIntent === 'add-to-cart' ? 1 : 0);
    expect(navigate).toHaveBeenCalledTimes(firstIntent === 'buy-now' ? 1 : 0);
  });

  it('加入購物車失敗時不導頁', async () => {
    const navigate = vi.fn();
    const onError = vi.fn();
    await executeCartIntent({
      intent: 'buy-now',
      selection: { skuId: 'sku-a', quantity: 1 },
      pendingRef: { current: false },
      addLine: async () => { throw new Error('offline'); },
      onStart: vi.fn(),
      onCartUpdated: vi.fn(),
      onAdded: vi.fn(),
      onNavigate: navigate,
      onError,
    });
    expect(navigate).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledTimes(1);
  });
});

describe('商品頁底部列接線', () => {
  const source = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), '..', '_components', 'AddToCartPanel.tsx'),
    'utf8',
  );

  it('兩顆按鈕共用同一份 disabled 與 loading 判斷', () => {
    expect(source.match(/disabled=\{actionDisabled\}/g)).toHaveLength(2);
    expect(source.match(/loading=\{actionLoading\}/g)).toHaveLength(2);
  });

  it('窄螢幕以 min-w-0 縮摘要、兩顆按鈕維持同列且不縮文字', () => {
    expect(source).toContain('flex min-w-0 flex-1 items-center');
    expect(source).toContain('min-w-0 flex-1 overflow-hidden');
    expect(source).toContain('flex shrink-0');
    expect(source).toContain('加入購物車');
    expect(source).toContain('立即購買');
  });
});
