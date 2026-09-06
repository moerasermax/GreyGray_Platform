/**
 * 訂單列表「出貨進度」欄的文案計算（#49）。
 *
 * 判斷本身用 `summarizeOrderShipments`（`../../shipments/_lib/orderShipments.ts`，
 * FE-29 已寫好、有測試，這裡不重寫一份）。這個檔案只負責把結果組成表格裡塞得下
 * 的一句短話——跟訂單詳情頁 `orderShipmentSummaryText` 的一整句是不同的呈現需求。
 */
import type { components } from '@greygray/api-client/admin';
import { summarizeOrderShipments } from '../../shipments/_lib/orderShipments';

type S = components['schemas'];

export function shipmentProgressText(
  orderId: string,
  orderStatus: S['OrderStatus'] | (string & {}),
  /** `null` 代表出貨單清單讀取失敗；還沒掛任何出貨單是空陣列，不是 `null`。 */
  shipments: readonly S['AdminShipment'][] | null,
  /** 出貨單清單還有下一頁，張數可能不完整——不能拿一頁的結果假裝是總數。 */
  truncated: boolean,
): string {
  if (orderStatus === 'Cancelled') {
    // 已取消的訂單不追蹤出貨進度：底下就算掛著已簽收的出貨單也不代表這張訂單「已出貨」。
    return '已取消';
  }
  if (shipments == null) {
    return '讀取失敗';
  }
  const summary = summarizeOrderShipments(shipments, orderId);
  const suffix = truncated ? '（可能不完整）' : '';
  if (summary.total === 0) {
    return `未建立${suffix}`;
  }
  if (summary.allDelivered) {
    return `全部已簽收${suffix}`;
  }
  return `${summary.total} 張 · 還差 ${summary.outstandingCount} 張簽收${suffix}`;
}
