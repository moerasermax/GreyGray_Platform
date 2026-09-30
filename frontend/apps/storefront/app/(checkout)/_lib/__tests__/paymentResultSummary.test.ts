import type { components } from '@greygray/api-client/storefront';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  COPY_ORDER_NUMBER_FAILED,
  COPY_ORDER_NUMBER_SUCCEEDED,
  MIXED_ORDER_HOLD_UNTIL_COMPLETE,
  MIXED_ORDER_SHIP_SEPARATELY,
  copyOrderNumber,
  formatPlacedAtInTaipei,
  hasMixedOrderModes,
  mixedOrderShippingMessage,
} from '../paymentResultSummary';

type S = components['schemas'];

const money = { amountMinor: 100, currency: 'TWD' } as const;

function line(mode: S['FulfillmentMode'], status: S['OrderLineStatus'] = 'Pending'): S['OrderLine'] {
  return {
    id: `${mode}-${status}`,
    skuId: `${mode}-sku`,
    productId: `${mode}-product`,
    name: mode,
    mode,
    status,
    quantity: 1,
    unitPrice: money,
    lineTotal: money,
  };
}

function summaryOrder(
  shippingPolicy: S['ShippingPolicy'],
  lines: S['OrderLine'][],
): Pick<S['Order'], 'shippingPolicy' | 'lines'> {
  return { shippingPolicy, lines };
}

const originalTimezone = process.env.TZ;

afterEach(() => {
  process.env.TZ = originalTimezone;
});

describe('付款結果的混合訂單摘要', () => {
  it('T1：現貨＋預購、分批寄送', () => {
    const order = summaryOrder('ShipSeparately', [line('Stock'), line('Preorder')]);
    expect(hasMixedOrderModes(order.lines)).toBe(true);
    expect(mixedOrderShippingMessage(order)).toBe(MIXED_ORDER_SHIP_SEPARATELY);
  });

  it('T2：現貨＋預購、到齊合併寄送', () => {
    const order = summaryOrder('HoldUntilComplete', [line('Stock'), line('Preorder')]);
    expect(hasMixedOrderModes(order.lines)).toBe(true);
    expect(mixedOrderShippingMessage(order)).toBe(MIXED_ORDER_HOLD_UNTIL_COMPLETE);
  });

  it('T3：純現貨即使是 ShipSeparately 也不是混合訂單', () => {
    const order = summaryOrder('ShipSeparately', [line('Stock')]);
    expect(hasMixedOrderModes(order.lines)).toBe(false);
    expect(mixedOrderShippingMessage(order)).toBeNull();
  });

  it('T3：Unavailable 的預購行不算有效模式', () => {
    const order = summaryOrder('ShipSeparately', [line('Stock'), line('Preorder', 'Unavailable')]);
    expect(hasMixedOrderModes(order.lines)).toBe(false);
    expect(mixedOrderShippingMessage(order)).toBeNull();
  });
});

describe('付款結果的台北下單時間', () => {
  it('T4：行程位於 UTC 時仍跨日顯示台北時間', () => {
    process.env.TZ = 'UTC';
    const formatted = formatPlacedAtInTaipei('2026-09-30T16:30:00Z');
    expect(formatted).toContain('2026/10/01');
    expect(formatted).toContain('00:30');
  });
});

describe('付款結果的訂單編號複製', () => {
  it('T8：成功後回傳已複製', async () => {
    const writeText = vi.fn(async () => {});
    await expect(copyOrderNumber('GG20260930001', writeText)).resolves.toBe(COPY_ORDER_NUMBER_SUCCEEDED);
    expect(writeText).toHaveBeenCalledWith('GG20260930001');
  });

  it('T8：剪貼簿拒絕時請使用者手動選取', async () => {
    const writeText = vi.fn(async () => {
      throw new Error('denied');
    });
    await expect(copyOrderNumber('GG20260930001', writeText)).resolves.toBe(COPY_ORDER_NUMBER_FAILED);
  });

  it('T8：剪貼簿不存在時請使用者手動選取', async () => {
    await expect(copyOrderNumber('GG20260930001', undefined)).resolves.toBe(COPY_ORDER_NUMBER_FAILED);
  });
});
