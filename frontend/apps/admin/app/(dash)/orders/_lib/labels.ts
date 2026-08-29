/**
 * 訂單相關 enum 的中文標籤與 `StatusPill` tone。
 *
 * 所有 `switch` 都有 `default`：後端新增列舉值不算破壞性變更，
 * 畫面要退回顯示原始字串而不是崩掉（`docs/06-前端工作包.md` 鐵則 5）。
 */
import { formatMoney } from '@greygray/api-client';
import type { StatusTone } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export function orderStatusLabel(status: S['OrderStatus'] | (string & {})): string {
  switch (status) {
    case 'AwaitingPayment':
      return '待付款';
    case 'PaidAwaitingClose':
      return '已付款．待截團';
    case 'ClosedAwaitingDeparture':
      return '已截團．待出發';
    case 'Purchasing':
      return '採購中';
    case 'GoodsReceived':
      return '已到貨';
    case 'ReadyToShip':
      return '待出貨';
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

export function orderStatusTone(status: S['OrderStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'AwaitingPayment':
      return 'warning';
    case 'PaidAwaitingClose':
    case 'ClosedAwaitingDeparture':
    case 'Purchasing':
    case 'GoodsReceived':
    case 'ReadyToShip':
      return 'info';
    case 'Shipped':
    case 'Completed':
      return 'success';
    case 'Cancelled':
      return 'neutral';
    default:
      return 'neutral';
  }
}

export function orderLineStatusLabel(status: S['OrderLineStatus'] | (string & {})): string {
  switch (status) {
    case 'Pending':
      return '待採購';
    case 'Reserved':
      return '已保留庫存';
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

export function orderLineStatusTone(status: S['OrderLineStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'Pending':
      return 'warning';
    case 'Reserved':
    case 'Purchased':
      return 'info';
    case 'Unavailable':
      return 'danger';
    case 'Shipped':
    case 'Completed':
      return 'success';
    case 'Cancelled':
      return 'neutral';
    default:
      return 'neutral';
  }
}

export function fulfillmentModeLabel(mode: S['FulfillmentMode'] | (string & {})): string {
  switch (mode) {
    case 'Stock':
      return '現貨';
    case 'Preorder':
      return '預購';
    default:
      return mode;
  }
}

export function deliveryMethodLabel(method: S['DeliveryMethod'] | (string & {})): string {
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

export function shippingPolicyLabel(policy: S['ShippingPolicy'] | (string & {})): string {
  switch (policy) {
    case 'ShipSeparately':
      return '現貨先出，預購到貨後再出（分批運費）';
    case 'HoldUntilComplete':
      return '等全部到齊再一起出（省一次運費）';
    default:
      return policy;
  }
}

export function paymentStatusLabel(status: S['PaymentStatus'] | (string & {})): string {
  switch (status) {
    case 'Pending':
      return '待收款';
    case 'Captured':
      return '已收款';
    case 'Failed':
      return '失敗';
    case 'Refunded':
      return '已全額退款';
    case 'PartiallyRefunded':
      return '部分退款';
    default:
      return status;
  }
}

export function paymentStatusTone(status: S['PaymentStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'Pending':
      return 'warning';
    case 'Captured':
      return 'success';
    case 'Failed':
      return 'danger';
    case 'Refunded':
    case 'PartiallyRefunded':
      return 'info';
    default:
      return 'neutral';
  }
}

export function paymentProviderLabel(provider: S['PaymentProvider'] | (string & {})): string {
  switch (provider) {
    case 'ECPay':
      return '綠界';
    case 'NewebPay':
      return '藍新';
    case 'LinePay':
      return 'LINE Pay';
    case 'ExternalSettled':
      return '線下結清';
    default:
      return provider;
  }
}

export function refundDestinationLabel(destination: S['RefundDestination'] | (string & {})): string {
  switch (destination) {
    case 'StoredValue':
      return '退成儲值金';
    case 'OriginalPaymentMethod':
      return '原路退回';
    default:
      return destination;
  }
}

/**
 * `refundedAmount` 是 `Money | null`。`null` 不是 0——沒退款就不顯示「$0」，回傳 `—`。
 */
export function refundedAmountText(refundedAmount: S['Money'] | null | undefined): string {
  if (refundedAmount == null) return '—';
  return formatMoney(refundedAmount);
}

export function refundDestinationHint(destination: S['RefundDestination'] | (string & {})): string {
  switch (destination) {
    case 'StoredValue':
      return '不動金流、零手續費，退款金額全額進客戶儲值金，下次消費可直接折抵。';
    case 'OriginalPaymentMethod':
      return '原路退回金流商，會被收取手續費，客戶實收金額可能因此比訂單金額少。';
    default:
      return '';
  }
}
