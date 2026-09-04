/**
 * 「這張訂單掛了幾張出貨單、還差幾張沒簽收」的純計算（#45）。
 *
 * 為什麼是純函式：這個 workspace 沒有 jsdom（前端四條之四），互動測不了，
 * 所以把判斷從畫面裡抽出來單獨測——訂單頁與建立出貨單對話框共用同一份規則，
 * 兩邊不會漂移。
 *
 * ── 規則的出處，不要在這裡重新發明 ──
 * ADR-025：Order 與 Shipment 是 N:M，一張訂單可以拆進多張出貨單；後端的
 * `RecordShipmentDeliveredAsync` 要求該訂單掛的出貨單**全部** `Delivered`
 * 才把訂單轉 `Shipped`。這裡只負責把這件事算出來讓畫面講得出口，
 * 不改規則本身。
 *
 * ── 「簽收」只認 `Delivered` ──
 * `Returned`（已退回）與 `Lost`（已遺失）都不是簽收，所以一樣算進「還沒簽收」。
 * 這跟後端一致：它比對的是 `Delivered`，不是「已經結案」。
 */
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

/** 已經不能再開新出貨單的訂單狀態。用 enum 字串，不對照數字。 */
const CLOSED_FOR_SHIPPING: ReadonlySet<S['OrderStatus']> = new Set([
  'Shipped',
  'Completed',
  'Cancelled',
]);

export interface OrderShipmentSummary {
  /** 掛在這張訂單上的出貨單，依取得順序。 */
  readonly shipments: readonly S['AdminShipment'][];
  readonly total: number;
  /** 狀態是 `Delivered` 的張數。 */
  readonly deliveredCount: number;
  /** 還沒簽收的張數（含已退回／已遺失）。 */
  readonly outstandingCount: number;
  /** 建了卻連交運都還沒做的張數——使用者最容易漏掉的那一種。 */
  readonly notDispatchedCount: number;
  /** 有出貨單而且**全部**簽收。零張時是 `false`（零張不叫「都好了」）。 */
  readonly allDelivered: boolean;
}

/**
 * 一張出貨單可以同時掛好幾張訂單，所以是陣列比對，不能假設只有一個。
 */
export function shipmentsOfOrder(
  shipments: readonly S['AdminShipment'][],
  orderId: string,
): readonly S['AdminShipment'][] {
  return shipments.filter((shipment) => shipment.orderIds.includes(orderId));
}

export function summarizeOrderShipments(
  shipments: readonly S['AdminShipment'][],
  orderId: string,
): OrderShipmentSummary {
  const mine = shipmentsOfOrder(shipments, orderId);
  const deliveredCount = mine.filter((shipment) => shipment.status === 'Delivered').length;
  const notDispatchedCount = mine.filter((shipment) => shipment.dispatchedAt == null).length;
  return {
    shipments: mine,
    total: mine.length,
    deliveredCount,
    outstandingCount: mine.length - deliveredCount,
    notDispatchedCount,
    allDelivered: mine.length > 0 && deliveredCount === mine.length,
  };
}

/**
 * 訂單頁那一句話。**數字全部來自 `summary`，沒有任何寫死的張數。**
 *
 * 使用者原本撞到的就是這件事：按了「已送達」訂單卻沒變，畫面一個字都沒說
 * 「還有兩張沒簽收」，於是以為出貨單沒建成功又再建一張。
 */
export function orderShipmentSummaryText(summary: OrderShipmentSummary): string {
  if (summary.total === 0) {
    return '還沒有建立出貨單。要出貨請到「出貨單」頁建立，一張出貨單可以合併多張訂單。';
  }
  if (summary.allDelivered) {
    return `這張訂單掛了 ${summary.total} 張出貨單，全部都已簽收。`;
  }
  const tail =
    summary.notDispatchedCount > 0
      ? `（其中 ${summary.notDispatchedCount} 張還沒交運）`
      : '';
  return (
    `這張訂單掛了 ${summary.total} 張出貨單，還有 ${summary.outstandingCount} 張沒有簽收${tail}；` +
    '全部簽收後訂單才會轉為已出貨。'
  );
}

/** 這張訂單現在還能不能再開一張出貨單。 */
export function canCreateShipmentForOrder(status: S['OrderStatus'] | (string & {})): boolean {
  return !CLOSED_FOR_SHIPPING.has(status as S['OrderStatus']);
}

/**
 * 建立出貨單時可以勾的訂單。
 *
 * **已經有出貨單的不會被排除**——拆單是合法的（ADR-025），只是畫面要標示
 * 「已有 N 張出貨單」讓人自己判斷。要排除的只有已出貨／已完成／已取消。
 */
export function selectableOrdersForShipment(
  orders: readonly S['AdminOrderListItem'][],
): readonly S['AdminOrderListItem'][] {
  return orders.filter((order) => canCreateShipmentForOrder(order.status));
}

/** 每張訂單各掛了幾張出貨單，給對話框標示用。 */
export function countShipmentsByOrderId(
  shipments: readonly S['AdminShipment'][],
): ReadonlyMap<string, number> {
  const counts = new Map<string, number>();
  for (const shipment of shipments) {
    for (const orderId of shipment.orderIds) {
      counts.set(orderId, (counts.get(orderId) ?? 0) + 1);
    }
  }
  return counts;
}
