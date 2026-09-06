/**
 * 出貨單列表「訂單」欄：講得出掛的是哪一張訂單，不是只有張數（#48）。
 *
 * 2026-09-04 使用者連著建了兩張出貨單（相隔四秒），日期、配送方式、狀態全一樣，
 * 結果把交運按在別張訂單的出貨單上——原本的「訂單數」欄只有 `{N} 張`。
 *
 * 純呈現：訂單編號的 map 在 `page.tsx` 取（`listOrders({ limit: 100 })`），這裡只負責畫。
 */
import Link from 'next/link';

export interface ShipmentOrderLinksCellProps {
  readonly orderIds: readonly string[];
  /** `orderId → orderNumber`。單一 id 查不到就退回 `orderId.slice(0, 8)`，不是空白。 */
  readonly orderNumberByOrderId: ReadonlyMap<string, string>;
  /**
   * 訂單清單整份讀取失敗——這時不逐一顯示 id（一串 uuid 前八碼看不出意義也點不出所以然），
   * 退回原本的「N 張」顯示。跟「清單讀得到、單一 id 查不到」是兩回事，後者仍然逐一顯示。
   */
  readonly ordersLoadFailed: boolean;
}

export function ShipmentOrderLinksCell({
  orderIds,
  orderNumberByOrderId,
  ordersLoadFailed,
}: ShipmentOrderLinksCellProps) {
  if (ordersLoadFailed) {
    return (
      <td className="px-3 py-2 text-fg">
        {orderIds.length} 張{orderIds.length > 1 ? '（合併出貨）' : ''}
      </td>
    );
  }

  return (
    <td className="px-3 py-2 text-fg">
      <span className="flex flex-wrap items-center gap-x-1 gap-y-0.5">
        {orderIds.map((orderId, index) => (
          <span key={orderId} className="flex items-center">
            {index > 0 ? <span className="mr-1 text-fg-muted">、</span> : null}
            <Link href={`/orders/${orderId}`} className="gg-numeric font-medium text-primary hover:underline">
              {orderNumberByOrderId.get(orderId) ?? orderId.slice(0, 8)}
            </Link>
          </span>
        ))}
        {orderIds.length > 1 ? (
          <span className="ml-1 text-xs text-fg-muted">（合併出貨，共 {orderIds.length} 張）</span>
        ) : null}
      </span>
    </td>
  );
}
