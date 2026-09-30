import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { components } from '@greygray/api-client/storefront';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  COPY_ORDER_NUMBER_FAILED,
  COPY_ORDER_NUMBER_SUCCEEDED,
  MIXED_ORDER_HOLD_UNTIL_COMPLETE,
  MIXED_ORDER_PICKUP_HOLD_UNTIL_COMPLETE,
  MIXED_ORDER_PICKUP_SEPARATELY,
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
  deliveryMethod: S['DeliveryMethod'] = 'HomeDelivery',
): Pick<S['Order'], 'shippingPolicy' | 'lines' | 'deliveryMethod'> {
  return { shippingPolicy, lines, deliveryMethod };
}

const originalTimezone = process.env.TZ;

afterEach(() => {
  if (originalTimezone === undefined) delete process.env.TZ;
  else process.env.TZ = originalTimezone;
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

  it.each([
    ['ShipSeparately', MIXED_ORDER_PICKUP_SEPARATELY],
    ['HoldUntilComplete', MIXED_ORDER_PICKUP_HOLD_UNTIL_COMPLETE],
  ] as const)('FE-54 T5：自取混合訂單 %s 使用取貨文案', (shippingPolicy, expected) => {
    const order = summaryOrder(shippingPolicy, [line('Stock'), line('Preorder')], 'SelfPickup');
    expect(mixedOrderShippingMessage(order)).toBe(expected);
    expect(expected).toContain('取貨');
    expect(expected).not.toContain('寄出');
  });
});

describe('付款結果頁原始碼守衛', () => {
  const pageSource = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'payment', 'result', 'page.tsx'),
    'utf8',
  );
  const code = pageSource.replace(/\/\*[^]*?\*\//g, '').replace(/\/\/.*$/gm, '');

  it('FE-54 T7：結果卡不再呼叫混合句函式', () => {
    expect(code).not.toContain('mixedOrderShippingMessage(');
  });

  it('FE-54 T7：複製狀態的 aria-live 不掛在 button 上', () => {
    expect(code).toContain('aria-label="複製訂單編號"');
    expect(code).toMatch(/<(span|p)[^>]*aria-live="polite"/);
    expect(code).not.toMatch(/<button[^>]*aria-live=/);
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
