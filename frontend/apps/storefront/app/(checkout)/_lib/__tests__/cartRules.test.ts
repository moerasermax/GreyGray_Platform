import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import {
  blockingAvailabilityWarning,
  evaluateCheckoutReadiness,
  requiresConvenienceStoreCode,
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

describe('requiresShippingAddress ／ requiresConvenienceStoreCode', () => {
  it('宅配才要地址，超商才要門市代號', () => {
    expect(requiresShippingAddress('HomeDelivery')).toBe(true);
    expect(requiresShippingAddress('ConvenienceStore')).toBe(false);
    expect(requiresShippingAddress('SelfPickup')).toBe(false);

    expect(requiresConvenienceStoreCode('ConvenienceStore')).toBe(true);
    expect(requiresConvenienceStoreCode('HomeDelivery')).toBe(false);
  });
});

describe('evaluateCheckoutReadiness', () => {
  const baseInput = {
    cart: { lines: [line()], hasMixedModes: false },
    deliveryMethod: null,
    shippingPolicy: null,
    shippingAddressId: null,
    convenienceStoreCode: null,
  } as const;

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

  it('條件都滿足時可以結帳', () => {
    const result = evaluateCheckoutReadiness({
      ...baseInput,
      deliveryMethod: 'SelfPickup',
    });
    expect(result).toEqual({ ready: true, reason: null });
  });
});
