import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import {
  blockingAvailabilityWarning,
  evaluateCheckoutReadiness,
  hasLoadedConvenienceStore,
  requiresConvenienceStore,
  requiresShippingAddress,
  requiresShippingPolicyChoice,
} from '../cartRules';

type S = components['schemas'];

function line(overrides: Partial<S['CartLine']> = {}): S['CartLine'] {
  return {
    id: 'line-1',
    skuId: 'sku-1',
    productId: 'product-1',
    name: '測試商品',
    variantName: null,
    imageUrl: null,
    mode: 'Stock',
    campaignId: null,
    campaignOfferId: null,
    quantity: 1,
    unitPrice: { amountMinor: 10000, currency: 'TWD' },
    lineTotal: { amountMinor: 10000, currency: 'TWD' },
    availabilityWarning: null,
    ...overrides,
  };
}

describe('blockingAvailabilityWarning', () => {
  it('沒有任何一項有警告時回 null', () => {
    expect(blockingAvailabilityWarning({ lines: [line(), line({ id: 'line-2' })] })).toBeNull();
  });

  it('驗收條件：有一項 availabilityWarning 時要回傳警告文字（畫面用它擋結帳鈕）', () => {
    const warned = line({ id: 'line-2', availabilityWarning: '庫存只剩 2 件' });
    expect(blockingAvailabilityWarning({ lines: [line(), warned] })).toBe('庫存只剩 2 件');
  });
});

describe('requiresShippingPolicyChoice', () => {
  it('hasMixedModes 為 true 時要選 shippingPolicy', () => {
    expect(requiresShippingPolicyChoice({ hasMixedModes: true })).toBe(true);
    expect(requiresShippingPolicyChoice({ hasMixedModes: false })).toBe(false);
  });
});

describe('requiresShippingAddress ／ requiresConvenienceStore', () => {
  it('宅配才要地址，超商才要門市', () => {
    expect(requiresShippingAddress('HomeDelivery')).toBe(true);
    expect(requiresShippingAddress('ConvenienceStore')).toBe(false);
    expect(requiresShippingAddress('SelfPickup')).toBe(false);

    expect(requiresConvenienceStore('ConvenienceStore')).toBe(true);
    expect(requiresConvenienceStore('HomeDelivery')).toBe(false);
  });
});

describe('hasLoadedConvenienceStore：讀到了門市才算選好（ADR-038）', () => {
  const TICKET = 'ABCDEFGHIJ0123456789';

  it('沒有票 → false', () => {
    expect(hasLoadedConvenienceStore(null, null)).toBe(false);
  });

  it('有票但還沒讀到（讀取中或讀失敗）→ false', () => {
    expect(hasLoadedConvenienceStore(TICKET, null)).toBe(false);
  });

  it('讀到的門市屬於另一張票 → false', () => {
    expect(hasLoadedConvenienceStore(TICKET, { selectionId: 'ZZZZZZZZZZZZZZZZZZZZ' })).toBe(false);
  });

  it('讀到了這張票的門市 → true', () => {
    expect(hasLoadedConvenienceStore(TICKET, { selectionId: TICKET })).toBe(true);
  });
});

describe('evaluateCheckoutReadiness', () => {
  const baseInput = {
    cart: { lines: [line()], hasMixedModes: false },
    deliveryMethod: null,
    shippingPolicy: null,
    shippingAddressId: null,
    convenienceStoreSelectionId: null,
    convenienceStoreSelection: null,
  } as const;
  const TICKET = 'ABCDEFGHIJ0123456789';

  it('空購物車擋下，且說明原因', () => {
    const result = evaluateCheckoutReadiness({ ...baseInput, cart: { lines: [], hasMixedModes: false } });
    expect(result.ready).toBe(false);
    expect(result.reason).toBeTruthy();
  });

  it('有 availabilityWarning 時擋下並顯示原因（驗收條件）', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      cart: { lines: [line({ availabilityWarning: '這個團已截止收單' })], hasMixedModes: false },
    });
    expect(result.ready).toBe(false);
    expect(result.reason).toBe('這個團已截止收單');
  });

  it('還沒選配送方式時擋下', () => {
    const result = evaluateCheckoutReadiness(baseInput);
    expect(result.ready).toBe(false);
  });

  it('混合訂單沒選 shippingPolicy 時擋下', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      cart: { lines: [line()], hasMixedModes: true },
      deliveryMethod: 'SelfPickup',
    });
    expect(result.ready).toBe(false);
    expect(result.reason).toContain('出貨方式');
  });

  it('宅配沒選地址時擋下；超商沒選門市時擋下', () => {
    expect(
      evaluateCheckoutReadiness({ ...baseInput, deliveryMethod: 'HomeDelivery' }).ready,
    ).toBe(false);
    expect(
      evaluateCheckoutReadiness({ ...baseInput, deliveryMethod: 'ConvenienceStore' }).ready,
    ).toBe(false);
  });

  it('超商取貨：有選店票 id 但還沒讀到門市 → 不能送，原因是「請選擇取貨門市。」', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      deliveryMethod: 'ConvenienceStore',
      convenienceStoreSelectionId: TICKET,
      convenienceStoreSelection: null,
    });
    expect(result).toEqual({ ready: false, reason: '請選擇取貨門市。' });
  });

  it('超商取貨：讀到了門市 → 可以送', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      deliveryMethod: 'ConvenienceStore',
      convenienceStoreSelectionId: TICKET,
      convenienceStoreSelection: { selectionId: TICKET },
    });
    expect(result).toEqual({ ready: true, reason: null });
  });

  it('宅配不受選店票影響：沒有門市也能送、有沒讀到的票也能送', () => {
    const home = { ...baseInput, deliveryMethod: 'HomeDelivery' as const, shippingAddressId: 'addr_1' };
    expect(evaluateCheckoutReadiness(home).ready).toBe(true);
    expect(evaluateCheckoutReadiness({ ...home, convenienceStoreSelectionId: TICKET }).ready).toBe(true);
  });

  it('條件都滿足時可以結帳', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      deliveryMethod: 'SelfPickup',
    });
    expect(result).toEqual({ ready: true, reason: null });
  });
});
