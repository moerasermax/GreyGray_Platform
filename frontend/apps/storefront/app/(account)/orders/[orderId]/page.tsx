'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Badge, Button, Card, ErrorState, PriceDisplay, Skeleton } from '@greygray/ui';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { ConfirmDialog } from '../../_components/ConfirmDialog';
import { OrderTimeline } from '../../_components/OrderTimeline';
import { PaymentCountdown } from '../../_components/PaymentCountdown';
import { isUnauthorized } from '../../_lib/authRedirect';
import { generalErrorMessage, traceIdOf } from '../../_lib/formErrors';
import {
  deliveryMethodLabel,
  orderLineStatusLabel,
  orderStatusLabel,
  shippingPolicyLabel,
} from '../../_lib/orderStatus';
import { submitPaymentForm } from '../../_lib/submitPaymentForm';

type Order = components['schemas']['Order'];
type Money = components['schemas']['Money'];

export default function OrderDetailPage() {
  const params = useParams<{ orderId: string }>();
  const orderId = params.orderId;
  const router = useRouter();

  const [order, setOrder] = useState<Order | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [cancelling, setCancelling] = useState(false);
  const [paying, setPaying] = useState(false);
  const [confirmCancelOpen, setConfirmCancelOpen] = useState(false);
  const [actionError, setActionError] = useState<unknown>(null);
  const paymentIdempotency = usePayloadIdempotency();
  const cancelIdempotency = usePayloadIdempotency();

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    storefrontApi
      .getOrder(browserApi(), orderId)
      .then(setOrder)
      .catch((caught: unknown) => {
        if (isUnauthorized(caught)) {
          router.replace('/login');
          return;
        }
        setError(caught);
      })
      .finally(() => setLoading(false));
  }, [orderId, router]);

  useEffect(() => {
    load();
  }, [load]);

  async function handleCancel() {
    setCancelling(true);
    setActionError(null);
    try {
      const body = {};
      const updated = await storefrontApi.cancelOrder(
        browserApi(),
        orderId,
        body,
        { idempotencyKey: cancelIdempotency.current({ orderId, body }) },
      );
      cancelIdempotency.complete();
      setOrder(updated);
      setConfirmCancelOpen(false);
    } catch (caught) {
      if (isUnauthorized(caught)) {
        router.replace('/login');
        return;
      }
      setActionError(caught);
    } finally {
      setCancelling(false);
    }
  }

  async function handlePay() {
    setPaying(true);
    setActionError(null);
    try {
      const initiation = await storefrontApi.initiatePayment(browserApi(), orderId, {
        idempotencyKey: paymentIdempotency.current({ orderId }),
      });
      paymentIdempotency.complete();
      // 送出後這個分頁會導去綠界，不用再手動 setPaying(false)。
      submitPaymentForm(initiation);
    } catch (caught) {
      if (isUnauthorized(caught)) {
        router.replace('/login');
        return;
      }
      setActionError(caught);
      setPaying(false);
    }
  }

  if (loading) {
    return (
      <main className="mx-auto flex max-w-[640px] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <Skeleton variant="block" className="h-[240px] w-full" />
      </main>
    );
  }

  if (error) {
    return (
      <main className="mx-auto flex max-w-[640px] flex-col px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <Card padding="none">
          <ErrorState title={generalErrorMessage(error)} traceId={traceIdOf(error)} onRetry={load} />
        </Card>
      </main>
    );
  }

  if (!order) return null;

  const canSelfCancel = order.status === 'AwaitingPayment';
  const canPay = order.status === 'AwaitingPayment';

  return (
    <main className="mx-auto flex max-w-[640px] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <Link href="/orders" className="text-[length:var(--gg-text-sm)] text-fg-muted">
        ← 回訂單列表
      </Link>

      <header className="flex flex-col gap-[var(--gg-space-2)]">
        <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
          <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">
            {order.orderNumber}
          </h1>
          <Badge variant={order.status} label={orderStatusLabel(order.status)} />
        </div>
        <p className="text-[length:var(--gg-text-xs)] text-fg-muted">
          {new Date(order.placedAt).toLocaleString('zh-TW')} 建立
        </p>
        {canPay && order.paymentDueAt && (
          <PaymentCountdown paymentDueAt={order.paymentDueAt} onExpire={load} />
        )}
      </header>

      <Card>
        <OrderTimeline status={order.status} />
      </Card>

      {actionError != null && (
        <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
          {generalErrorMessage(actionError)}
        </p>
      )}

      {(canPay || canSelfCancel) && (
        <div className="flex flex-wrap gap-[var(--gg-space-3)]">
          {canPay && (
            <Button variant="primary" onClick={handlePay} loading={paying}>
              前往付款
            </Button>
          )}
          {canSelfCancel && (
            <Button variant="secondary" onClick={() => setConfirmCancelOpen(true)}>
              取消訂單
            </Button>
          )}
        </div>
      )}

      <Card className="flex flex-col gap-[var(--gg-space-4)]">
        <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">商品明細</h2>
        <div className="flex flex-col gap-[var(--gg-space-4)]">
          {order.lines.map((line) => (
            <div
              key={line.id}
              className="flex flex-col gap-[var(--gg-space-1)] border-b border-border-soft pb-[var(--gg-space-3)] last:border-b-0 last:pb-0"
            >
              <div className="flex items-start justify-between gap-[var(--gg-space-3)]">
                <div>
                  <p className="font-bold text-fg">{line.name}</p>
                  {line.variantName && (
                    <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{line.variantName}</p>
                  )}
                  <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
                    {orderLineStatusLabel(line.status)}・數量 {line.quantity}
                  </p>
                </div>
                <PriceDisplay amount={line.lineTotal} size="sm" />
              </div>
              {line.status === 'Unavailable' && (
                <p className="flex flex-wrap items-center gap-[var(--gg-space-1)] text-[length:var(--gg-text-sm)] text-warning-text">
                  <span>這項商品現場缺貨，已退款</span>
                  {line.refundedAmount && <PriceDisplay amount={line.refundedAmount} size="sm" />}
                  <span>，其餘品項照常出貨。</span>
                </p>
              )}
            </div>
          ))}
        </div>
        <div className="flex flex-col gap-[var(--gg-space-1)] border-t border-border-soft pt-[var(--gg-space-3)]">
          <MoneyRow label="商品小計" amount={order.goodsTotal} />
          <MoneyRow label="運費" amount={order.shippingFee} />
          <MoneyRow label="含運總額" amount={order.grandTotal} emphasize />
          {order.paidAmount && <MoneyRow label="已付金額" amount={order.paidAmount} />}
        </div>
      </Card>

      <Card className="flex flex-col gap-[var(--gg-space-2)]">
        <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">配送方式</h2>
        <p className="text-[length:var(--gg-text-sm)] text-fg">{deliveryMethodLabel(order.deliveryMethod)}</p>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{shippingPolicyLabel(order.shippingPolicy)}</p>
        {order.shippingAddress && (
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
            {order.shippingAddress.recipientName}・{order.shippingAddress.phoneNumber}
            <br />
            {order.shippingAddress.postalCode} {order.shippingAddress.city}
            {order.shippingAddress.district}
            {order.shippingAddress.streetAddress}
          </p>
        )}
        {order.convenienceStoreName && (
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">取貨門市：{order.convenienceStoreName}</p>
        )}
      </Card>

      {order.quoteExplain && order.quoteExplain.length > 0 && (
        <details className="text-[length:var(--gg-text-sm)] text-fg-muted">
          <summary className="cursor-pointer font-bold text-fg">運費說明</summary>
          <ul className="mt-[var(--gg-space-2)] flex list-disc flex-col gap-[var(--gg-space-1)] pl-[var(--gg-space-5)]">
            {order.quoteExplain.map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
        </details>
      )}

      <ConfirmDialog
        open={confirmCancelOpen}
        title="取消這張訂單？"
        description="取消後無法復原，商品需要的話請重新下單。"
        confirmLabel="確定取消"
        danger
        loading={cancelling}
        onConfirm={handleCancel}
        onCancel={() => setConfirmCancelOpen(false)}
      />
    </main>
  );
}

function MoneyRow({ label, amount, emphasize }: { label: string; amount: Money; emphasize?: boolean }) {
  return (
    <div className="flex items-center justify-between">
      <span className={emphasize ? 'font-bold text-fg' : 'text-[length:var(--gg-text-sm)] text-fg-muted'}>
        {label}
      </span>
      <PriceDisplay amount={amount} size={emphasize ? 'md' : 'sm'} />
    </div>
  );
}
