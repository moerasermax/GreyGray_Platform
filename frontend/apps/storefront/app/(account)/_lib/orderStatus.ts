/**
 * 訂單狀態的顯示邏輯。九個狀態共用一條主線，`Cancelled` 刻意不在主線上
 * （FE-5 做法要點）——它是主線之外的終止狀態，不是主線的最後一步。
 *
 * **`switch` 一定要有 `default`**：後端新增狀態不算破壞性變更（`docs/05-API契約.md` §6），
 * 前端沒見過的字串要照原字串顯示，不能崩掉、不能顯示空白。
 */

/** 主線順序。`Purchasing → GoodsReceived` 那段是預購才會停留久的階段，
 * 現貨會很快從 `PaidAwaitingClose` 直接跳到 `ReadyToShip` 附近，但主線本身兩種模式共用。 */
export const ORDER_MAIN_LINE = [
  'AwaitingPayment',
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
] as const;

export type OrderTimelineStepState = 'done' | 'current' | 'upcoming';

export interface OrderTimelineStep {
  readonly status: string;
  readonly label: string;
  readonly state: OrderTimelineStepState;
}

export interface OrderTimeline {
  readonly steps: OrderTimelineStep[];
  /** `true` 代表這張訂單已取消，主線僅供參考，畫面要另外標示，不要當成走到某一步。 */
  readonly isCancelled: boolean;
  readonly rawStatus: string;
}

export function orderStatusLabel(status: string): string {
  switch (status) {
    case 'AwaitingPayment':
      return '待付款';
    case 'PaidAwaitingClose':
      return '已付款，等待截團';
    case 'ClosedAwaitingDeparture':
      return '已截團，等待出發採購';
    case 'Purchasing':
      return '採購中';
    case 'GoodsReceived':
      return '已收貨';
    case 'ReadyToShip':
      return '準備出貨';
    case 'Shipped':
      return '已出貨';
    case 'Completed':
      return '已完成';
    case 'Cancelled':
      return '已取消';
    default:
      // 未知值：顯示原始字串，不當成錯誤（docs/05-API契約.md §6）。
      return status;
  }
}

export function orderLineStatusLabel(status: string): string {
  switch (status) {
    case 'Pending':
      return '處理中';
    case 'Reserved':
      return '已保留';
    case 'Purchased':
      return '已採購';
    case 'Unavailable':
      return '缺貨已退款';
    case 'Shipped':
      return '已出貨';
    case 'Completed':
      return '已完成';
    case 'Cancelled':
      return '已取消';
    default:
      return status;
  }
}

export function deliveryMethodLabel(method: string): string {
  switch (method) {
    case 'ConvenienceStore':
      return '超商取貨';
    case 'HomeDelivery':
      return '宅配到府';
    case 'SelfPickup':
      return '自取';
    default:
      return method;
  }
}

export function shippingPolicyLabel(policy: string): string {
  switch (policy) {
    case 'ShipSeparately':
      return '現貨先出（分批寄送）';
    case 'HoldUntilComplete':
      return '等回國一起出（合併寄送）';
    default:
      return policy;
  }
}

export function buildOrderTimeline(status: string): OrderTimeline {
  if (status === 'Cancelled') {
    return {
      steps: ORDER_MAIN_LINE.map((s) => ({ status: s, label: orderStatusLabel(s), state: 'upcoming' })),
      isCancelled: true,
      rawStatus: status,
    };
  }

  const index = ORDER_MAIN_LINE.findIndex((s) => s === status);

  // 未知狀態（含 M1b 之後新增的狀態）：主線全部顯示成 upcoming，不猜它落在哪一步，
  // 但畫面仍然要能正常畫出來、顯示原始字串，不能崩掉。
  const steps: OrderTimelineStep[] = ORDER_MAIN_LINE.map((s, i) => ({
    status: s,
    label: orderStatusLabel(s),
    state: index === -1 ? 'upcoming' : i < index ? 'done' : i === index ? 'current' : 'upcoming',
  }));

  return { steps, isCancelled: false, rawStatus: status };
}
