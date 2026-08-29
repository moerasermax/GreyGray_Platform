/**
 * 出貨單（M1b，`/v1/shipments`）的 mock 假資料。獨立於 `fixtures.admin.ts`（FE-9 的檔案），
 * 只讀它匯出的 `adminOrders`，不改它一個字。
 *
 * **Order 與 Shipment 是 N:M**（契約 `docs/api/openapi.admin.yaml` `/v1/shipments` POST 的
 * description 明寫「這是日常，不是邊緣案例」），所以這份 fixture 刻意示範兩種形狀：
 *   - 一單多包：`GG260828021` 拆進 `homeDeliverySplitDraft` 與 `homeDeliverySplitDispatched` 兩張出貨單。
 *   - 一包多單：`convenienceMerged` 一張出貨單裡合併了 `GG26082800031` 與 `GG260828022` 兩張訂單。
 *
 * `fixtures.admin.ts` 目前沒有任何訂單處在 `ReadyToShip`／`GoodsReceived`——
 * 那是「已截團、已到貨」之後才會出現的狀態，而目前的訂單 fixture 都停在更早或更晚的階段。
 * 這裡沿用既有訂單各自的狀態示範出貨單，不去猜或竄改訂單狀態；已知落差回報給整合者，
 * 不在這一包補（那是 FE-9 的檔案）。
 */
import type { components } from '../types.admin';
import { adminOrders } from './fixtures.admin';
import { hexId } from './ids';

type S = components['schemas'];

function twd(major: number): S['Money'] {
  return { amountMinor: Math.round(major * 100), currency: 'TWD' };
}

function orderIdOf(orderNumber: string): string {
  const order = adminOrders.find((o) => o.orderNumber === orderNumber);
  if (!order) throw new Error(`fixtures.admin.shipments：找不到訂單 ${orderNumber}，fixtures.admin.ts 是不是變了？`);
  return order.id;
}

const hoursAgoIso = (hours: number) => new Date(Date.now() - hours * 3_600_000).toISOString();

export const SHIPMENT_IDS = {
  homeDeliverySplitDraft: hexId('shipment:宅配分批-草稿'),
  homeDeliverySplitDispatched: hexId('shipment:宅配分批-已交運'),
  convenienceMerged: hexId('shipment:超商合併-已送達'),
  selfPickupPacked: hexId('shipment:自取-已打包'),
} as const;

/** `GG260828021` 拆成兩包：一包還沒交運，一包已經在路上。示範「一單多包」。 */
const homeDeliverySplitDraft: S['AdminShipment'] = {
  id: SHIPMENT_IDS.homeDeliverySplitDraft,
  method: 'HomeDelivery',
  status: 'Draft',
  trackingNumber: null,
  orderIds: [orderIdOf('GG260828021')],
  carrierCost: null,
  dispatchedAt: null,
  deliveredAt: null,
};

const homeDeliverySplitDispatched: S['AdminShipment'] = {
  id: SHIPMENT_IDS.homeDeliverySplitDispatched,
  method: 'HomeDelivery',
  status: 'Dispatched',
  trackingNumber: 'SF1234567890TW',
  orderIds: [orderIdOf('GG260828021')],
  carrierCost: twd(85),
  dispatchedAt: hoursAgoIso(20),
  deliveredAt: null,
};

/** `GG26082800031`（混合狀態）與 `GG260828022`（已完成）合併出貨省一次運費。示範「一包多單」。 */
const convenienceMerged: S['AdminShipment'] = {
  id: SHIPMENT_IDS.convenienceMerged,
  method: 'ConvenienceStore',
  status: 'Delivered',
  trackingNumber: '7-11-88293015',
  orderIds: [orderIdOf('GG26082800031'), orderIdOf('GG260828022')],
  carrierCost: twd(35),
  dispatchedAt: hoursAgoIso(72),
  deliveredAt: hoursAgoIso(24),
};

const selfPickupPacked: S['AdminShipment'] = {
  id: SHIPMENT_IDS.selfPickupPacked,
  method: 'SelfPickup',
  status: 'Packed',
  trackingNumber: null,
  orderIds: [orderIdOf('GG260828023')],
  carrierCost: null,
  dispatchedAt: null,
  deliveredAt: null,
};

export const adminShipments: S['AdminShipment'][] = [
  homeDeliverySplitDraft,
  homeDeliverySplitDispatched,
  convenienceMerged,
  selfPickupPacked,
];
