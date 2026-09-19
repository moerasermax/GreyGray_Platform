/**
 * 訂單詳情頁的收件人顯示（ADR-039）。
 *
 * **快照優先，快照為 null 才退回地址簿。**
 * `order.recipientName`／`recipientPhone` 是下單當時凍結的快照，只有 ADR-039 之後
 * 成立的訂單才有值；那之前的舊訂單這兩個欄位是 `null`——如果不退回，舊訂單的「收件人」
 * 那一行會整段消失，客人反而看不到本來看得到的資訊，是功能倒退，不是行為改進。
 * 退回 `shippingAddress` 是**目前**地址簿的資料，不是下單當時的，畫面要標出來。
 */
export interface OrderRecipientLike {
  readonly recipientName?: string | null;
  readonly recipientPhone?: string | null;
  readonly shippingAddress?: {
    readonly recipientName: string;
    readonly phoneNumber: string;
  } | null;
}

export interface RecipientDisplay {
  readonly recipientName: string;
  readonly recipientPhone: string;
  /** true＝沒有下單當時凍結的快照，這是退回目前地址簿的值（ADR-039 之前的舊訂單）。 */
  readonly isFallback: boolean;
}

/** 快照兩個欄位都沒有、也沒有地址簿可退回時（例如舊的非宅配訂單），回 `null`——什麼都不顯示。 */
export function recipientDisplayOf(order: OrderRecipientLike): RecipientDisplay | null {
  if (order.recipientName || order.recipientPhone) {
    return {
      recipientName: order.recipientName ?? '',
      recipientPhone: order.recipientPhone ?? '',
      isFallback: false,
    };
  }
  if (order.shippingAddress) {
    return {
      recipientName: order.shippingAddress.recipientName,
      recipientPhone: order.shippingAddress.phoneNumber,
      isFallback: true,
    };
  }
  return null;
}
