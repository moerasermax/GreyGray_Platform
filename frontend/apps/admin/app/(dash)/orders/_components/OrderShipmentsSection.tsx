/**
 * 訂單頁的「出貨單」區塊（#45）。
 *
 * 純呈現：畫什麼完全由 props 決定，取數與過濾在 `page.tsx`，判斷在
 * `../../shipments/_lib/orderShipments.ts`。沒有 jsdom，所以文案靠
 * `renderToStaticMarkup` 驗（做法見 `../__tests__/orderShipmentsSection.test.tsx`）。
 *
 * 狀態標籤與 tone 直接用 `shipments/_lib/labels.ts` 現成的，不在這裡重寫一份——
 * 兩份會漂移。
 */
import type { components } from '@greygray/api-client/admin';
import { StatusPill } from '@greygray/ui/admin';
import Link from 'next/link';
import {
  orderShipmentSummaryText,
  summarizeOrderShipments,
} from '../../shipments/_lib/orderShipments';
import { shipmentMethodLabel, shipmentStatusLabel, shipmentStatusTone } from '../../shipments/_lib/labels';
import { formatTaipeiDateTime } from '../_lib/paymentDetails';

type S = components['schemas'];

export interface OrderShipmentsSectionProps {
  readonly orderId: string;
  /** 已取得的出貨單（**未過濾**，元件自己挑出掛在這張訂單上的）。 */
  readonly shipments: readonly S['AdminShipment'][];
  readonly loading: boolean;
  readonly loadFailed: boolean;
  /**
   * 來源清單還有下一頁。`true` 時要講清楚張數可能不完整——
   * 不能拿一頁的結果假裝是總數。
   */
  readonly truncated: boolean;
  readonly onRetry: () => void;
}

function timeText(value: string | null | undefined, fallback: string): string {
  if (!value) return fallback;
  return formatTaipeiDateTime(value);
}

export function OrderShipmentsSection({
  orderId,
  shipments,
  loading,
  loadFailed,
  truncated,
  onRetry,
}: OrderShipmentsSectionProps) {
  const summary = summarizeOrderShipments(shipments, orderId);

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-sm font-semibold text-fg-muted">出貨單</h2>

      {loading ? (
        <div className="rounded-card border border-border-soft bg-surface px-4 py-3 text-sm text-fg-muted">
          讀取出貨單中…
        </div>
      ) : loadFailed ? (
        <div className="flex flex-wrap items-center gap-3 rounded-card border border-border-soft bg-surface px-4 py-3 text-sm text-fg-muted">
          <span>讀取出貨單失敗，這一區塊暫時看不到；訂單本身的資料不受影響。</span>
          <button
            type="button"
            onClick={onRetry}
            className="rounded-full border border-primary px-3 py-1 text-xs font-semibold text-primary-text hover:bg-primary-subtle"
          >
            重試
          </button>
        </div>
      ) : (
        <>
          <p className="rounded-card border border-border-soft bg-surface-sunken px-4 py-3 text-sm text-fg">
            {orderShipmentSummaryText(summary)}
          </p>
          {truncated ? (
            <p className="text-xs text-fg-muted">
              只讀了出貨單列表的第一頁，若這張訂單有更早的出貨單，上面的張數可能不完整。
            </p>
          ) : null}
          {summary.total > 0 ? (
            <ul className="flex flex-col gap-2">
              {summary.shipments.map((shipment) => (
                <li
                  key={shipment.id}
                  className="flex flex-wrap items-center justify-between gap-3 rounded-card border border-border-soft bg-surface px-4 py-2.5 shadow-card"
                >
                  <span className="flex flex-wrap items-center gap-3">
                    <Link
                      href={`/shipments/${shipment.id}`}
                      className="gg-numeric font-medium text-primary-text hover:underline"
                    >
                      {shipment.id.slice(0, 8)}
                    </Link>
                    <StatusPill
                      label={shipmentStatusLabel(shipment.status)}
                      tone={shipmentStatusTone(shipment.status)}
                    />
                    <span className="text-sm text-fg-muted">{shipmentMethodLabel(shipment.method)}</span>
                    {shipment.orderIds.length > 1 ? (
                      <span className="text-xs text-fg-muted">
                        與其他 {shipment.orderIds.length - 1} 張訂單合併出貨
                      </span>
                    ) : null}
                  </span>
                  <span className="flex flex-wrap items-center gap-4 text-xs text-fg-muted">
                    <span className="gg-numeric">追蹤單號 {shipment.trackingNumber ?? '—'}</span>
                    <span>交運 {timeText(shipment.dispatchedAt, '尚未交運')}</span>
                    <span>送達 {timeText(shipment.deliveredAt, '尚未送達')}</span>
                  </span>
                </li>
              ))}
            </ul>
          ) : null}
        </>
      )}
    </section>
  );
}
