/**
 * #45 的迴歸測試：訂單留在「待出貨」而底下已經有三張出貨單時，
 * 畫面必須算得出「還差幾張沒簽收」。
 *
 * 為什麼有這個檔案：使用者 2026-09-04 回報「按了已送達訂單也不會更新」，
 * 查下去發現規則是對的（ADR-025：全部簽收才轉已出貨），錯的是畫面一個字都沒說。
 * 這裡把那個判斷釘住——沒有 jsdom，所以判斷抽成純函式再測（前端四條之四）。
 *
 * 樣本直接照抄 Leader 從正式機查到的那一組（派工書 §0.1 的三張）。
 */
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/admin';
import {
  canCreateShipmentForOrder,
  countShipmentsByOrderId,
  orderShipmentSummaryText,
  selectableOrdersForShipment,
  shipmentsOfOrder,
  summarizeOrderShipments,
} from './orderShipments';

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

/** 正式機那三張：一張建了沒動、一張已送達、一張只交運。 */
const LIVE_THREE: readonly S['AdminShipment'][] = [
  shipment('01a0682d-0000-7000-8000-000000000001', 'Draft'),
  shipment('01a0682e-0000-7000-8000-000000000002', 'Delivered', {
    dispatchedAt: '2026-09-04T00:50:52Z',
    deliveredAt: '2026-09-04T00:51:02Z',
  }),
  shipment('01a0682e-0000-7000-8000-000000000003', 'Dispatched', {
    method: 'ConvenienceStore',
    dispatchedAt: '2026-09-04T00:51:29Z',
  }),
];

describe('summarizeOrderShipments：這張訂單掛了幾張、還差幾張沒簽收', () => {
  it('0 張：不算「全部簽收」，outstanding 也是 0', () => {
    const summary = summarizeOrderShipments([], ORDER_ID);

    expect(summary.total).toBe(0);
    expect(summary.outstandingCount).toBe(0);
    expect(summary.allDelivered).toBe(false);
  });

  it('全部簽收：allDelivered 為 true，還差 0 張', () => {
    const all = [
      shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z', dispatchedAt: '2026-09-03T00:00:00Z' }),
      shipment('s2', 'Delivered', { deliveredAt: '2026-09-04T01:00:00Z', dispatchedAt: '2026-09-03T01:00:00Z' }),
    ];

    const summary = summarizeOrderShipments(all, ORDER_ID);

    expect(summary.total).toBe(2);
    expect(summary.deliveredCount).toBe(2);
    expect(summary.outstandingCount).toBe(0);
    expect(summary.allDelivered).toBe(true);
  });

  it('★ 部分簽收（正式機那三張）：3 張、還差 2 張、其中 1 張沒交運', () => {
    const summary = summarizeOrderShipments(LIVE_THREE, ORDER_ID);

    expect(summary.total).toBe(3);
    expect(summary.deliveredCount).toBe(1);
    expect(summary.outstandingCount).toBe(2);
    expect(summary.notDispatchedCount).toBe(1);
    expect(summary.allDelivered).toBe(false);
  });

  it('建了沒交運那一張：算進「還沒簽收」也算進「還沒交運」', () => {
    const summary = summarizeOrderShipments([shipment('s1', 'Draft')], ORDER_ID);

    expect(summary.total).toBe(1);
    expect(summary.outstandingCount).toBe(1);
    expect(summary.notDispatchedCount).toBe(1);
  });

  it('已退回／已遺失不是簽收，一樣算「還沒簽收」（後端比對的是 Delivered）', () => {
    const summary = summarizeOrderShipments(
      [
        shipment('s1', 'Returned', { dispatchedAt: '2026-09-03T00:00:00Z' }),
        shipment('s2', 'Lost', { dispatchedAt: '2026-09-03T00:00:00Z' }),
      ],
      ORDER_ID,
    );

    expect(summary.deliveredCount).toBe(0);
    expect(summary.outstandingCount).toBe(2);
    expect(summary.allDelivered).toBe(false);
  });
});

describe('shipmentsOfOrder：一張出貨單可以掛好幾張訂單', () => {
  it('用陣列比對，合併出貨的那張兩邊都算得到', () => {
    const merged = shipment('s-merged', 'Dispatched', { orderIds: [OTHER_ORDER_ID, ORDER_ID] });
    const otherOnly = shipment('s-other', 'Draft', { orderIds: [OTHER_ORDER_ID] });

    expect(shipmentsOfOrder([merged, otherOnly], ORDER_ID).map((s) => s.id)).toEqual(['s-merged']);
    expect(shipmentsOfOrder([merged, otherOnly], OTHER_ORDER_ID).map((s) => s.id)).toEqual([
      's-merged',
      's-other',
    ]);
  });
});

describe('orderShipmentSummaryText：畫面上那一句話', () => {
  it('0 張時也要有話講，不是空白', () => {
    const text = orderShipmentSummaryText(summarizeOrderShipments([], ORDER_ID));

    expect(text).toContain('還沒有建立出貨單');
  });

  it('★ 部分簽收時要講「全部簽收後訂單才會轉為已出貨」，張數從資料算出來', () => {
    const text = orderShipmentSummaryText(summarizeOrderShipments(LIVE_THREE, ORDER_ID));

    expect(text).toContain('3 張出貨單');
    expect(text).toContain('還有 2 張沒有簽收');
    expect(text).toContain('其中 1 張還沒交運');
    expect(text).toContain('全部簽收後訂單才會轉為已出貨');
  });

  it('全部簽收時不再叫人等，也不出現「還有 N 張」', () => {
    const text = orderShipmentSummaryText(
      summarizeOrderShipments([shipment('s1', 'Delivered', { deliveredAt: '2026-09-04T00:00:00Z' })], ORDER_ID),
    );

    expect(text).toContain('全部都已簽收');
    expect(text).not.toContain('沒有簽收');
  });
});

describe('selectableOrdersForShipment：哪些訂單還能出貨', () => {
  function order(id: string, status: S['OrderStatus']): S['AdminOrderListItem'] {
    return {
      id,
      orderNumber: `GG${id}`,
      customerDisplayName: '王小美',
      status,
      grandTotal: { amountMinor: 100000, currency: 'TWD' },
      placedAt: '2026-09-01T00:00:00Z',
    };
  }

  it('已出貨／已完成／已取消排除，待出貨留下', () => {
    const orders = [
      order('1', 'ReadyToShip'),
      order('2', 'Shipped'),
      order('3', 'Completed'),
      order('4', 'Cancelled'),
      order('5', 'GoodsReceived'),
      order('6', 'AwaitingPayment'),
    ];

    expect(selectableOrdersForShipment(orders).map((o) => o.id)).toEqual(['1', '5', '6']);
  });

  it('canCreateShipmentForOrder 逐個狀態', () => {
    expect(canCreateShipmentForOrder('ReadyToShip')).toBe(true);
    expect(canCreateShipmentForOrder('Shipped')).toBe(false);
    expect(canCreateShipmentForOrder('Completed')).toBe(false);
    expect(canCreateShipmentForOrder('Cancelled')).toBe(false);
    // 後端新增列舉值不算破壞性變更：認不得的一律當成還能出，不要無聲吃掉一張訂單。
    expect(canCreateShipmentForOrder('SomethingNew')).toBe(true);
  });
});

describe('countShipmentsByOrderId：對話框那一行「已有 N 張出貨單」', () => {
  it('合併出貨的一張要同時記到每一張訂單頭上', () => {
    const counts = countShipmentsByOrderId([
      shipment('s1', 'Draft', { orderIds: [ORDER_ID, OTHER_ORDER_ID] }),
      shipment('s2', 'Delivered', { orderIds: [ORDER_ID] }),
    ]);

    expect(counts.get(ORDER_ID)).toBe(2);
    expect(counts.get(OTHER_ORDER_ID)).toBe(1);
    expect(counts.get('沒出過貨的訂單')).toBeUndefined();
  });
});
