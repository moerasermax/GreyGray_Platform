/**
 * 訂單列表「出貨進度」欄的文案（#49）。
 *
 * `summarizeOrderShipments` 本身的邊界（0 張／全部簽收／部分簽收／建了沒交運）
 * 已經在 `../../shipments/_lib/orderShipments.test.ts` 釘住，這裡只補呈現層那一半：
 * 這句話怎麼從 summary 組出來，以及 summary 算不出來（讀取失敗）、不該算（已取消）
 * 這兩種它自己要處理的情況。
 */
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import { shipmentProgressText } from './shipmentProgress';

type S = components['schemas'];

const ORDER_ID = '0199f0c2-0000-7000-8000-0000000000a1';
const OTHER_ORDER_ID = '0199f0c2-0000-7000-8000-0000000000a2';

function shipment(
  id: string,
  status: S['ShipmentStatus'],
  extra: Partial<S['AdminShipment']> = {},
): S['AdminShipment'] {
  return {
    id,
    method: 'HomeDelivery',
    status,
    trackingNumber: null,
    orderIds: [ORDER_ID],
    carrierCost: null,
    dispatchedAt: null,
    deliveredAt: null,
    ...extra,
  };
}

describe('shipmentProgressText：訂單列表「出貨進度」欄的文案（#49）', () => {
  it('沒有出貨單時講「未建立」，不是空白', () => {
    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', [], false)).toBe('未建立');
  });

  it('全部簽收時講「全部已簽收」', () => {
    const shipments = [shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' })];

    expect(shipmentProgressText(ORDER_ID, 'Shipped', shipments, false)).toBe('全部已簽收');
  });

  it('★ 部分簽收：張數 ＋ 還差幾張，格式照派工書 §1 必做 B 的例句', () => {
    const shipments = [
      shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' }),
      shipment('s2', 'Dispatched', { dispatchedAt: '2026-09-04T00:00:00Z' }),
      shipment('s3', 'Draft'),
    ];

    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', shipments, false)).toBe('3 張 · 還差 2 張簽收');
  });

  it('建了沒交運那一張一樣算進「還差幾張」', () => {
    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', [shipment('s1', 'Draft')], false)).toBe(
      '1 張 · 還差 1 張簽收',
    );
  });

  it('★ 已取消的訂單不顯示會誤導的進度文字，即使底下掛著已簽收的出貨單', () => {
    const shipments = [shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' })];

    expect(shipmentProgressText(ORDER_ID, 'Cancelled', shipments, false)).toBe('已取消');
  });

  it('★ 一張出貨單掛多張訂單：只算自己這張訂單的部分，兩邊各自看得到', () => {
    const merged = shipment('s-merged', 'Dispatched', { orderIds: [OTHER_ORDER_ID, ORDER_ID], dispatchedAt: '2026-09-04T00:00:00Z' });

    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', [merged], false)).toBe('1 張 · 還差 1 張簽收');
    expect(shipmentProgressText(OTHER_ORDER_ID, 'ReadyToShip', [merged], false)).toBe('1 張 · 還差 1 張簽收');
  });

  it('★ 出貨單清單讀取失敗時顯示「讀取失敗」，不是拋例外或空白', () => {
    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', null, false)).toBe('讀取失敗');
  });

  it('讀取失敗時已取消的訂單仍然優先講「已取消」', () => {
    expect(shipmentProgressText(ORDER_ID, 'Cancelled', null, false)).toBe('已取消');
  });

  it('清單還有下一頁時附註「可能不完整」，不假裝張數是總數', () => {
    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', [shipment('s1', 'Draft')], true)).toBe(
      '1 張 · 還差 1 張簽收（可能不完整）',
    );
    expect(shipmentProgressText(ORDER_ID, 'ReadyToShip', [], true)).toBe('未建立（可能不完整）');
  });
});
