'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Badge, Button, Card, ErrorState, Skeleton } from '@greygray/ui';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { ConfirmDialog } from '../../_components/ConfirmDialog';
import { OrderTimeline } from '../../_components/OrderTimeline';
import { PaymentCountdown } from '../../_components/PaymentCountdown';
import { PaymentInstructionsCard } from '../_components/PaymentInstructionsCard';
import { isUnauthorized, loginHrefForCurrentPage } from '../../_lib/authRedirect';
import { generalErrorMessage, traceIdOf } from '../../_lib/formErrors';
import {
  orderStatusLabel,
} from '../../_lib/orderStatus';
import { submitPaymentForm } from '../../_lib/submitPaymentForm';
import { OrderSummary } from '../_components/OrderSummary';
import {
  orderPaymentPresentation,
  shouldReloadOrderAfterCancelError,
  shouldReloadOrderAfterPaymentError,
} from '../../../(checkout)/_lib/paymentInstructions';
import { formatPlacedAtInTaipei } from '../../../(checkout)/_lib/paymentResultSummary';
import { CancellationNotice, PaymentOverdueNotice } from '../../../(checkout)/_lib/orderPaymentNotices';

type Order = components['schemas']['Order'];

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
  const [expiredPaymentKey, setExpiredPaymentKey] = useState<string | null>(null);
  const paymentIdempotency = usePayloadIdempotency();
  const cancelIdempotency = usePayloadIdempotency();

  const load = useCallback((background = false) => {
    if (!background) setLoading(true);
    setError(null);
    return storefrontApi
      .getOrder(browserApi(), orderId)
      .then(setOrder)
      .catch((caught: unknown) => {
        if (isUnauthorized(caught)) {
          router.replace(loginHrefForCurrentPage());
          return;
        }
        setError(caught);
      })
      .finally(() => {
        if (!background) setLoading(false);
      });
  }, [orderId, router]);

  useEffect(() => {
    void load();
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
        router.replace(loginHrefForCurrentPage());
        return;
      }
      if (shouldReloadOrderAfterCancelError(caught)) {
        cancelIdempotency.complete();
        setConfirmCancelOpen(false);
        await load(true);
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
        router.replace(loginHrefForCurrentPage());
        return;
      }
      if (shouldReloadOrderAfterPaymentError(caught)) {
        paymentIdempotency.complete();
        setPaying(false);
        await load(true);
        return;
      }
      setActionError(caught);
      setPaying(false);
    }
  }

  function handlePaymentCountdownExpire() {
    if (!order?.paymentDueAt) return;
    const key = `${order.id}:${order.paymentDueAt}`;
    if (expiredPaymentKey === key) return;
    setExpiredPaymentKey(key);
    void load(true);
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
          <ErrorState title={generalErrorMessage(error)} traceId={traceIdOf(error)} onRetry={() => void load()} />
        </Card>
      </main>
    );
  }

  if (!order) return null;

  const canSelfCancel = order.status === 'AwaitingPayment';
  const paymentPresentation = orderPaymentPresentation(order);
  const canPay = paymentPresentation.canPay;
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
          {formatPlacedAtInTaipei(order.placedAt)}（台灣時間）建立
        </p>
        {paymentPresentation.showCountdown && order.paymentDueAt && (
          expiredPaymentKey === `${order.id}:${order.paymentDueAt}`
            ? <p className="text-[length:var(--gg-text-sm)] text-fg-muted">付款期限已到，系統處理中</p>
            : <PaymentCountdown paymentDueAt={order.paymentDueAt} onExpire={handlePaymentCountdownExpire} />
        )}
      </header>

      {order.status === 'Cancelled' && <CancellationNotice order={order} />}
      {paymentPresentation.showOverdue && <PaymentOverdueNotice />}

      {!paymentPresentation.showOverdue && order.paymentInstructions != null && <PaymentInstructionsCard order={order} />}

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

      <OrderSummary order={order} variant="detail" />

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
        description={paymentPresentation.showOverdue
          ? '取消後無法復原，商品需要的話請重新下單。如果你已經繳費，取消後款項會由我們辦理退款。'
          : '取消後無法復原，商品需要的話請重新下單。'}
        confirmLabel="確定取消"
        danger
        loading={cancelling}
        onConfirm={handleCancel}
        onCancel={() => setConfirmCancelOpen(false)}
      />
    </main>
  );
}
