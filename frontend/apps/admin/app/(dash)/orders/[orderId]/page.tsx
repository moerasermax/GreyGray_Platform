'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import {
  cancelOrder,
  cancelOrderLine,
  getCampaign,
  getOrder,
  recordManualRefund,
  refundOrderLineShortfall,
} from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import {
  DataTable,
  ErrorState,
  MoneyCell,
  StatusPill,
  useToast,
  type DataTableColumn,
} from '@greygray/ui/admin';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { Fragment, useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../login/_lib/session';
import { listShipments } from '../../shipments/_lib/api';
import { CancelOrderDialog } from '../_components/CancelOrderDialog';
import { CancelOrderLineDialog } from '../_components/CancelOrderLineDialog';
import { OrderShipmentsSection } from '../_components/OrderShipmentsSection';
import { ManualRefundDialog } from '../_components/ManualRefundDialog';
import { ManualRefundSection } from '../_components/ManualRefundSection';
import { PaymentInstructions } from '../_components/PaymentInstructions';
import { RefundShortfallDialog } from '../_components/RefundShortfallDialog';
import {
  deliveryMethodLabel,
  fulfillmentModeLabel,
  orderLineStatusLabel,
  orderLineStatusTone,
  orderStatusLabel,
  orderStatusTone,
  orderCancellationSourceLabel,
  paymentMethodLabel,
  paymentProviderLabel,
  paymentStatusLabel,
  paymentStatusTone,
  refundedAmountText,
  shippingPolicyLabel,
} from '../_lib/labels';
import { convenienceStoreDisplay } from '../_lib/convenienceStore';
import { recipientAddressOf } from '../_lib/recipientAddress';
import {
  canRecordManualRefund,
  formatTaipeiDateTime,
  manualRefundErrorMessage,
  taipeiToday,
  validateManualRefundInput,
} from '../_lib/paymentDetails';

type S = components['schemas'];
type OrderLine = S['AdminOrderLine'];

/** 「取貨門市」那一格（ADR-038）：名稱（代號）、下一行地址。文字判斷在 `_lib/convenienceStore.ts`。 */
function ConvenienceStoreCell({ order }: { order: S['AdminOrder'] }) {
  const store = convenienceStoreDisplay(order);
  return (
    <div>
      <p className="text-xs text-fg-muted">取貨門市</p>
      {store.primary ? <p className="mt-1 text-sm font-medium text-fg">{store.primary}</p> : null}
      {store.address ? <p className="mt-1 text-xs text-fg-muted">{store.address}</p> : null}
    </div>
  );
}

export default function OrderDetailPage() {
  const params = useParams<{ orderId: string }>();
  const orderId = params.orderId;
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  // 出貨單需要符合 Operator（與側邊欄「出貨」的 `requiredRole` 一致）。角色不符時
  // 不打 `/v1/shipments`（唯讀、會計原本會撞 403），區塊改顯示一句中性說明（FE-49）。
  // `(dash)/layout.tsx` 拿到員工資料前不渲染子頁，所以 `getSession()` 這裡一定有值。
  const staffRole = getSession()?.role ?? 'ReadOnly';
  const canViewShipments = hasRequiredRole(staffRole, 'Operator');
  // 這頁三個寫入端點後端都掛 `StaffRoleFilter(Operator)`（後端樹 Admin Host 的
  // `M1aEndpoints.cs` 177–223 行、`M1bShortfallRefundEndpoints.cs`）：
  // 取消整張訂單、取消此品項、退短缺款。角色不符時按鈕不渲染，不讓人按了才 403（FE-50）。
  const canOperate = hasRequiredRole(staffRole, 'Operator');
  const canRecordRefund = canRecordManualRefund(staffRole);

  const [order, setOrder] = useState<S['AdminOrder'] | null>(null);
  const [campaignTitle, setCampaignTitle] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // 出貨單區塊（#45）。契約沒有「依訂單查出貨單」的篩選，撈一頁在前端用
  // `orderIds` 比對——同一個模式在 `shipments/[shipmentId]/page.tsx` 第 46-49 行
  // 已經用了。M1b 的資料量吃得下；有下一頁時區塊會自己講「張數可能不完整」，
  // 不會拿一頁的結果假裝是總數。
  const [shipments, setShipments] = useState<readonly S['AdminShipment'][]>([]);
  const [shipmentsLoading, setShipmentsLoading] = useState(true);
  const [shipmentsFailed, setShipmentsFailed] = useState(false);
  const [shipmentsTruncated, setShipmentsTruncated] = useState(false);
  const [shipmentsReloadKey, setShipmentsReloadKey] = useState(0);

  const [cancelOrderOpen, setCancelOrderOpen] = useState(false);
  const [cancelLine, setCancelLine] = useState<OrderLine | null>(null);
  const [refundShortfallLine, setRefundShortfallLine] = useState<OrderLine | null>(null);
  const [manualRefundPaymentId, setManualRefundPaymentId] = useState<string | null>(null);
  const [manualRefundAmount, setManualRefundAmount] = useState('');
  const [manualRefundDate, setManualRefundDate] = useState(() => taipeiToday(new Date()));
  const [manualRefundNote, setManualRefundNote] = useState('');
  const [manualRefundError, setManualRefundError] = useState<string | null>(null);
  const [manualRefundSubmitting, setManualRefundSubmitting] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void getOrder(browserApi(), orderId)
      .then((found) => {
        if (!cancelled) setOrder(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取訂單失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [orderId, reloadKey]);

  useEffect(() => {
    if (!order?.campaignId) {
      setCampaignTitle(null);
      return;
    }
    let cancelled = false;
    void getCampaign(browserApi(), order.campaignId).then(
      (campaign) => {
        if (!cancelled) setCampaignTitle(campaign.title);
      },
      () => {
        if (!cancelled) setCampaignTitle(null);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [order?.campaignId]);

  // 讀失敗不擋整頁：訂單本身已經在上面那個 effect 讀到了，出貨單拿不到只讓
  // 這一區塊自己顯示錯誤（理由同第 91-93 行 `getCampaign` 那段）。
  useEffect(() => {
    if (!canViewShipments) return;
    let cancelled = false;
    setShipmentsLoading(true);
    setShipmentsFailed(false);
    void listShipments(browserApi(), { limit: 100 })
      .then((page) => {
        if (cancelled) return;
        setShipments(page.items);
        setShipmentsTruncated(page.nextCursor != null);
      })
      .catch(() => {
        if (!cancelled) setShipmentsFailed(true);
      })
      .finally(() => {
        if (!cancelled) setShipmentsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [canViewShipments, orderId, shipmentsReloadKey]);

  async function handleCancelOrder(input: { reason: string; refundTo: S['RefundDestination'] }) {
    const payload = { orderId, kind: 'order', input };
    const updated = await cancelOrder(browserApi(), orderId, input, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    setOrder(updated);
    toast.show('success', '訂單已取消，退款已依選擇的去處處理。');
  }

  async function handleCancelLine(input: { reason: string; refundTo: S['RefundDestination'] }) {
    if (!cancelLine) return;
    const payload = { orderId, kind: 'line', lineId: cancelLine.id, input };
    const updated = await cancelOrderLine(browserApi(), orderId, cancelLine.id, input, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    setOrder(updated);
    toast.show('success', `品項「${cancelLine.name}」已取消。`);
  }

  async function handleRefundShortfall(input: { reason: string; refundTo: S['RefundDestination'] }) {
    if (!refundShortfallLine) return;
    const payload = { orderId, kind: 'refund-shortfall', lineId: refundShortfallLine.id, input };
    const updated = await refundOrderLineShortfall(
      browserApi(),
      orderId,
      refundShortfallLine.id,
      input,
      { idempotencyKey: idempotency.current(payload) },
    );
    idempotency.complete();
    setOrder(updated);
    toast.show('success', `品項「${refundShortfallLine.name}」的短缺款已退款。`);
  }

  function openManualRefund(paymentId: string) {
    setManualRefundPaymentId(paymentId);
    setManualRefundAmount('');
    setManualRefundDate(taipeiToday(new Date()));
    setManualRefundNote('');
    setManualRefundError(null);
  }

  function closeManualRefund() {
    if (manualRefundSubmitting) return;
    setManualRefundPaymentId(null);
    setManualRefundError(null);
  }

  async function handleManualRefund() {
    if (!manualRefundPaymentId) return;
    const validation = validateManualRefundInput(
      manualRefundAmount,
      manualRefundDate,
      manualRefundNote,
      new Date(),
    );
    if (!validation.ok) {
      setManualRefundError(validation.error);
      return;
    }
    const payload = { orderId, paymentId: manualRefundPaymentId, body: validation.body };
    setManualRefundSubmitting(true);
    setManualRefundError(null);
    try {
      const updated = await recordManualRefund(browserApi(), orderId, manualRefundPaymentId, validation.body, {
        idempotencyKey: idempotency.current(payload),
      });
      idempotency.complete();
      setOrder(updated);
      setManualRefundPaymentId(null);
      toast.show('success', '已登記人工退款匯款。');
    } catch (cause) {
      const mapped = manualRefundErrorMessage(cause);
      setManualRefundError(
        mapped ?? (cause instanceof ApiError ? cause.problem.title : '登記失敗，請稍後再試。'),
      );
    } finally {
      setManualRefundSubmitting(false);
    }
  }

  if (loading) {
    return <div className="h-40 animate-pulse rounded-card bg-surface-sunken" />;
  }

  if (error || !order) {
    return (
      <ErrorState
        title={error instanceof ApiError ? error.problem.title : (error?.message ?? '找不到這張訂單。')}
        traceId={error instanceof ApiError ? error.shortTraceId : null}
        onRetry={() => setReloadKey((current) => current + 1)}
      />
    );
  }

  const lineActionColumn: DataTableColumn<OrderLine> = {
    key: 'action',
    header: '操作',
    renderCell: (line) => {
      const canRefundShortfall = (line.quantityShortfall ?? 0) > 0 && line.refundedAmount == null;
      return (
        <td className="px-3 py-2">
          <div className="flex flex-col items-start gap-1">
            {line.status === 'Cancelled' || line.status === 'Unavailable' ? (
              <span className="text-xs text-fg-muted">已處理</span>
            ) : (
              <button
                type="button"
                onClick={() => setCancelLine(line)}
                className="rounded-full border border-danger/30 px-3 py-1 text-xs font-semibold text-danger hover:bg-danger-subtle"
              >
                取消此品項
              </button>
            )}
            {canRefundShortfall ? (
              <button
                type="button"
                onClick={() => setRefundShortfallLine(line)}
                className="rounded-full border border-primary px-3 py-1 text-xs font-semibold text-primary-text hover:bg-primary-subtle"
              >
                退短缺款
              </button>
            ) : null}
          </div>
        </td>
      );
    },
  };

  const lineColumns: DataTableColumn<OrderLine>[] = [
    {
      key: 'name',
      header: '品項',
      renderCell: (line) => (
        <td className="px-3 py-2">
          <div className="font-medium text-fg">{line.name}</div>
          {line.variantName ? <div className="text-xs text-fg-muted">{line.variantName}</div> : null}
        </td>
      ),
    },
    {
      key: 'mode',
      header: '模式',
      renderCell: (line) => <td className="px-3 py-2 text-fg-muted">{fulfillmentModeLabel(line.mode)}</td>,
    },
    {
      key: 'status',
      header: '狀態',
      renderCell: (line) => (
        <td className="px-3 py-2">
          <StatusPill label={orderLineStatusLabel(line.status)} tone={orderLineStatusTone(line.status)} />
        </td>
      ),
    },
    {
      key: 'quantity',
      header: '數量',
      headerAlign: 'right',
      renderCell: (line) => <td data-numeric className="gg-numeric px-3 py-2">{line.quantity}</td>,
    },
    {
      key: 'unitPrice',
      header: '單價',
      headerAlign: 'right',
      renderCell: (line) => <MoneyCell value={formatMoney(line.unitPrice)} />,
    },
    {
      key: 'lineTotal',
      header: '小計',
      headerAlign: 'right',
      renderCell: (line) => <MoneyCell value={formatMoney(line.lineTotal)} />,
    },
    {
      key: 'refundedAmount',
      header: '缺貨退款',
      headerAlign: 'right',
      renderCell: (line) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          <div>{refundedAmountText(line.refundedAmount)}</div>
          {(line.quantityShortfall ?? 0) > 0 ? (
            <div className="text-xs text-fg-muted">短缺 {line.quantityShortfall} 件</div>
          ) : null}
        </td>
      ),
    },
    // 「操作」欄整欄只給符合 Operator 的角色：欄裡兩個按鈕後端都要 Operator，
    // 沒權限的人看到一欄全是「—」只是噪音，直接不放這一欄。
    ...(canOperate ? [lineActionColumn] : []),
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/orders" className="text-sm text-primary-text hover:underline">
            ← 回訂單列表
          </Link>
          {/* 不用 `.gg-numeric`：它含 `text-align: right` 且不在 cascade layer 裡，
              Tailwind 的 `text-left` 蓋不過；標題與下方資訊區只要等寬數字，靠左（FE-49）。 */}
          <h1 className="mt-1 font-mono text-xl font-semibold tabular-nums text-fg">{order.orderNumber}</h1>
          <div className="mt-1 flex items-center gap-2">
            <StatusPill label={orderStatusLabel(order.status)} tone={orderStatusTone(order.status)} />
            <span className="text-sm text-fg-muted">
              下單於{' '}
              {formatTaipeiDateTime(order.placedAt)}
            </span>
          </div>
        </div>
        {canOperate && order.status !== 'Cancelled' ? (
          <button
            type="button"
            onClick={() => setCancelOrderOpen(true)}
            className="rounded-full border border-danger/30 bg-danger-subtle px-4 py-1.5 text-sm font-semibold text-danger hover:opacity-90"
          >
            取消整張訂單
          </button>
        ) : null}
      </div>

      <section className="grid grid-cols-1 gap-4 rounded-card border border-border-soft bg-surface p-5 shadow-card sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <p className="text-xs text-fg-muted">客戶</p>
          <p className="mt-1 text-sm font-medium text-fg">{order.customerDisplayName}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">收件人</p>
          <p className="mt-1 select-all text-sm font-medium text-fg">{order.recipientName ?? '—'}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">收件人手機</p>
          <p className="mt-1 select-all font-mono text-sm font-medium tabular-nums text-fg">{order.recipientPhone ?? '—'}</p>
        </div>
        {recipientAddressOf(order) ? (
          <div>
            <p className="text-xs text-fg-muted">收件地址</p>
            <p className="mt-1 select-all text-sm font-medium text-fg">{recipientAddressOf(order)}</p>
          </div>
        ) : null}
        <div>
          <p className="text-xs text-fg-muted">配送方式</p>
          <p className="mt-1 text-sm font-medium text-fg">{deliveryMethodLabel(order.deliveryMethod)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">出貨政策</p>
          <p className="mt-1 text-sm font-medium text-fg">{shippingPolicyLabel(order.shippingPolicy)}</p>
        </div>
        {order.deliveryMethod === 'ConvenienceStore' ? <ConvenienceStoreCell order={order} /> : null}
        {order.paymentDueAt ? (
          <div>
            <p className="text-xs text-fg-muted">繳費期限</p>
            <p className="mt-1 text-sm font-medium text-fg">{formatTaipeiDateTime(order.paymentDueAt)}</p>
          </div>
        ) : null}
        {order.cancellationSource ? (
          <div>
            <p className="text-xs text-fg-muted">取消來源</p>
            <p className="mt-1 text-sm font-medium text-fg">{orderCancellationSourceLabel(order.cancellationSource)}</p>
          </div>
        ) : null}
        <div>
          <p className="text-xs text-fg-muted">商品小計</p>
          <p className="mt-1 font-mono text-sm font-semibold tabular-nums text-fg">{formatMoney(order.goodsTotal)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">運費</p>
          <p className="mt-1 font-mono text-sm font-semibold tabular-nums text-fg">{formatMoney(order.shippingFee)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">含運總額</p>
          <p className="mt-1 font-mono text-base font-semibold tabular-nums text-fg">{formatMoney(order.grandTotal)}</p>
        </div>
        {order.campaignId ? (
          <div>
            <p className="text-xs text-fg-muted">所屬開團</p>
            <p className="mt-1 text-sm font-medium text-fg">{campaignTitle ?? order.campaignId}</p>
          </div>
        ) : null}
      </section>

      {order.quoteExplain && order.quoteExplain.length > 0 ? (
        <section className="rounded-card border border-border-soft bg-surface-sunken p-4">
          <p className="text-xs font-medium text-fg-muted">計費說明</p>
          <ul className="mt-1 list-inside list-disc text-sm text-fg">
            {order.quoteExplain.map((line, index) => (
              // eslint-disable-next-line react/no-array-index-key
              <li key={index}>{line}</li>
            ))}
          </ul>
        </section>
      ) : null}

      <section className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">
          品項（{order.lines.length} 項，狀態各自獨立——缺貨的、已出貨的、待採購的可能同時存在）
        </h2>
        <DataTable
          columns={lineColumns}
          rows={order.lines}
          getRowKey={(line) => line.id}
          emptyTitle="這張訂單沒有品項"
        />
      </section>

      {canViewShipments ? (
        <OrderShipmentsSection
          orderId={orderId}
          shipments={shipments}
          loading={shipmentsLoading}
          loadFailed={shipmentsFailed}
          truncated={shipmentsTruncated}
          onRetry={() => setShipmentsReloadKey((current) => current + 1)}
        />
      ) : (
        <section className="flex flex-col gap-3">
          <h2 className="text-sm font-semibold text-fg-muted">出貨單</h2>
          <p className="rounded-card border border-border-soft bg-surface px-4 py-3 text-sm text-fg-muted">
            出貨單需要營運以上權限才能查看。
          </p>
        </section>
      )}

      {order.payments && order.payments.length > 0 ? (
        <section className="flex flex-col gap-3">
          <h2 className="text-sm font-semibold text-fg-muted">付款紀錄</h2>
          <div className="overflow-x-auto rounded-card border border-border-soft bg-surface shadow-card">
            <table className="w-full min-w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-border-soft bg-surface-sunken text-left text-fg-on-tint">
                  <th scope="col" className="px-3 py-2 text-xs font-medium">金流商</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">付款方式</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">狀態</th>
                  <th scope="col" className="px-3 py-2 text-right text-xs font-medium" data-numeric>金額</th>
                  <th scope="col" className="px-3 py-2 text-right text-xs font-medium" data-numeric>手續費</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">收款時間</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">撥款時間</th>
                </tr>
              </thead>
              <tbody>
                {order.payments.map((payment) => (
                  <Fragment key={payment.id}>
                    <tr className="border-b border-border-soft">
                      <td className="px-3 py-2 text-fg">{paymentProviderLabel(payment.provider)}</td>
                      <td className="px-3 py-2 text-fg">{payment.method ? paymentMethodLabel(payment.method) : '—'}</td>
                      <td className="px-3 py-2">
                        <StatusPill label={paymentStatusLabel(payment.status)} tone={paymentStatusTone(payment.status)} />
                      </td>
                      <MoneyCell value={formatMoney(payment.amount)} />
                      <MoneyCell value={payment.fee ? formatMoney(payment.fee) : '—'} />
                      <td className="px-3 py-2 text-fg-muted">{payment.capturedAt ? formatTaipeiDateTime(payment.capturedAt) : '—'}</td>
                      <td className="px-3 py-2 text-fg-muted">{payment.settledAt ? formatTaipeiDateTime(payment.settledAt) : '尚未撥款'}</td>
                    </tr>
                    {payment.instructions || payment.manualRefund ? (
                      <tr className="border-b border-border-soft last:border-0">
                        <td colSpan={7} className="px-3 py-3">
                          <div className="flex flex-col gap-3">
                            <PaymentInstructions instructions={payment.instructions} paymentStatus={payment.status} orderStatus={order.status} />
                            <ManualRefundSection paymentId={payment.id} manualRefund={payment.manualRefund} canRecord={canRecordRefund} onRecord={openManualRefund} />
                          </div>
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      <CancelOrderDialog
        open={cancelOrderOpen}
        orderNumber={order.orderNumber}
        onClose={() => setCancelOrderOpen(false)}
        onConfirm={handleCancelOrder}
      />
      <CancelOrderLineDialog
        open={cancelLine !== null}
        lineName={cancelLine?.name ?? ''}
        onClose={() => setCancelLine(null)}
        onConfirm={handleCancelLine}
      />
      <RefundShortfallDialog
        open={refundShortfallLine !== null}
        lineName={refundShortfallLine?.name ?? ''}
        shortfallQuantity={refundShortfallLine?.quantityShortfall ?? 0}
        onClose={() => setRefundShortfallLine(null)}
        onConfirm={handleRefundShortfall}
      />
      <ManualRefundDialog
        open={manualRefundPaymentId !== null}
        amountInput={manualRefundAmount}
        remittedOn={manualRefundDate}
        noteInput={manualRefundNote}
        today={taipeiToday(new Date())}
        error={manualRefundError}
        submitting={manualRefundSubmitting}
        onAmountChange={setManualRefundAmount}
        onRemittedOnChange={setManualRefundDate}
        onNoteChange={setManualRefundNote}
        onClose={closeManualRefund}
        onSubmit={() => void handleManualRefund()}
      />
    </div>
  );
}
