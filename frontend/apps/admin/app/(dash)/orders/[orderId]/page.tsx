'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import {
  cancelOrder,
  cancelOrderLine,
  getCampaign,
  getOrder,
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
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { CancelOrderDialog } from '../_components/CancelOrderDialog';
import { CancelOrderLineDialog } from '../_components/CancelOrderLineDialog';
import { MaskedContactNote } from '../_components/MaskedContactNote';
import { RefundShortfallDialog } from '../_components/RefundShortfallDialog';
import {
  deliveryMethodLabel,
  fulfillmentModeLabel,
  orderLineStatusLabel,
  orderLineStatusTone,
  orderStatusLabel,
  orderStatusTone,
  paymentProviderLabel,
  paymentStatusLabel,
  paymentStatusTone,
  refundedAmountText,
  shippingPolicyLabel,
} from '../_lib/labels';

type S = components['schemas'];
type OrderLine = S['AdminOrderLine'];

export default function OrderDetailPage() {
  const params = useParams<{ orderId: string }>();
  const orderId = params.orderId;
  const toast = useToast();
  const idempotency = usePayloadIdempotency();

  const [order, setOrder] = useState<S['AdminOrder'] | null>(null);
  const [campaignTitle, setCampaignTitle] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [cancelOrderOpen, setCancelOrderOpen] = useState(false);
  const [cancelLine, setCancelLine] = useState<OrderLine | null>(null);
  const [refundShortfallLine, setRefundShortfallLine] = useState<OrderLine | null>(null);

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
    {
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
                  className="rounded-full border border-primary/30 px-3 py-1 text-xs font-semibold text-primary hover:bg-primary-subtle"
                >
                  退短缺款
                </button>
              ) : null}
            </div>
          </td>
        );
      },
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/orders" className="text-sm text-primary hover:underline">
            ← 回訂單列表
          </Link>
          <h1 className="mt-1 gg-numeric text-xl font-semibold text-fg">{order.orderNumber}</h1>
          <div className="mt-1 flex items-center gap-2">
            <StatusPill label={orderStatusLabel(order.status)} tone={orderStatusTone(order.status)} />
            <span className="text-sm text-fg-muted">
              下單於{' '}
              {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(
                new Date(order.placedAt),
              )}
            </span>
          </div>
        </div>
        {order.status !== 'Cancelled' ? (
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
          <p className="text-xs text-fg-muted">聯絡方式</p>
          <div className="mt-1">
            <MaskedContactNote maskedContact={order.customerContactMasked} />
          </div>
        </div>
        <div>
          <p className="text-xs text-fg-muted">配送方式</p>
          <p className="mt-1 text-sm font-medium text-fg">{deliveryMethodLabel(order.deliveryMethod)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">出貨政策</p>
          <p className="mt-1 text-sm font-medium text-fg">{shippingPolicyLabel(order.shippingPolicy)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">商品小計</p>
          <p className="gg-numeric mt-1 text-sm font-semibold text-fg">{formatMoney(order.goodsTotal)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">運費</p>
          <p className="gg-numeric mt-1 text-sm font-semibold text-fg">{formatMoney(order.shippingFee)}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">含運總額</p>
          <p className="gg-numeric mt-1 text-base font-semibold text-fg">{formatMoney(order.grandTotal)}</p>
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

      {order.payments && order.payments.length > 0 ? (
        <section className="flex flex-col gap-3">
          <h2 className="text-sm font-semibold text-fg-muted">付款紀錄</h2>
          <div className="overflow-x-auto rounded-card border border-border-soft bg-surface shadow-card">
            <table className="w-full min-w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-border-soft bg-surface-sunken text-left text-fg-on-tint">
                  <th scope="col" className="px-3 py-2 text-xs font-medium">金流商</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">狀態</th>
                  <th scope="col" className="px-3 py-2 text-right text-xs font-medium" data-numeric>金額</th>
                  <th scope="col" className="px-3 py-2 text-right text-xs font-medium" data-numeric>手續費</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">收款時間</th>
                  <th scope="col" className="px-3 py-2 text-xs font-medium">撥款時間</th>
                </tr>
              </thead>
              <tbody>
                {order.payments.map((payment) => (
                  <tr key={payment.id} className="border-b border-border-soft last:border-0">
                    <td className="px-3 py-2 text-fg">{paymentProviderLabel(payment.provider)}</td>
                    <td className="px-3 py-2">
                      <StatusPill label={paymentStatusLabel(payment.status)} tone={paymentStatusTone(payment.status)} />
                    </td>
                    <MoneyCell value={formatMoney(payment.amount)} />
                    <MoneyCell value={payment.fee ? formatMoney(payment.fee) : '—'} />
                    <td className="px-3 py-2 text-fg-muted">
                      {payment.capturedAt
                        ? new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(
                            new Date(payment.capturedAt),
                          )
                        : '—'}
                    </td>
                    <td className="px-3 py-2 text-fg-muted">
                      {payment.settledAt
                        ? new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(
                            new Date(payment.settledAt),
                          )
                        : '尚未撥款'}
                    </td>
                  </tr>
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
    </div>
  );
}
