/**
 * 後台訂單詳情「收件地址」那一格（ADR-039）。宅配訂單下單當時凍結的完整單行字串；
 * 超商取貨訂單與 ADR-039 之前的舊訂單一律是 `null`，那一行本來就不該顯示——
 * 後台沒有這個欄位以前，宅配訂單在後台看不到地址，等於寄不出去。
 *
 * **不做 fallback**：後台契約沒有 `shippingAddress`，而且拿一個可能已經被客人
 * 改過的地址去寄，比讓出貨的人去問客人更危險（Leader 裁決，見驗收紀錄）。
 */
export interface RecipientAddressLike {
  readonly recipientAddress?: string | null;
}

/** 空字串當作沒有；有值才回傳，讓呼叫端只用它來判斷要不要畫這一格。 */
export function recipientAddressOf(order: RecipientAddressLike): string | null {
  return typeof order.recipientAddress === 'string' && order.recipientAddress.trim() !== ''
    ? order.recipientAddress
    : null;
}
