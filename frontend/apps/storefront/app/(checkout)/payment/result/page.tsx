'use client';

/*
 * 付款結果頁。**假設**：綠界的 `OrderResultURL`／`ClientBackURL` 會被後端設成
 * 導回這裡並帶 `?orderId=`（見交付回報「假設」一節——這兩個欄位在 `PaymentInitiation.fields`
 * 裡是不透明字串，前端看不到,也不該猜）。
 *
 * **不相信網址上任何綠界帶回來的參數**，一律重新 `GET /v1/orders/{orderId}`
 * 問後端目前狀態——綠界的回呼是非同步的，瀏覽器導回來的當下不保證 webhook 已處理完。
 */

import Link from 'next/link';
import { Suspense, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { Button, Card, ErrorState, PriceDisplay, Skeleton } from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../../_lib/apiClient';
import { ExplainDisclosure } from '../../_components/ExplainDisclosure';
import { describeError, type ErrorDisplay } from '../../_lib/errorDisplay';

type S = components['schemas'];

const PAID_STATUSES: ReadonlySet<S['OrderStatus']> = new Set([
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
]);

const RESULT_SKELETON = (
  <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
    <Skeleton variant="block" className="h-48 w-full" />
  </main>
);

/** `useSearchParams()` 要求 Suspense 邊界，否則 `next build` 靜態化這頁時會報錯。 */
export default function PaymentResultPage() {
  return (
    <Suspense fallback={RESULT_SKELETON}>
      <PaymentResultContent />
    </Suspense>
  );
}

function PaymentResultContent() {
  const searchParams = useSearchParams();
  const orderId = searchParams.get('orderId');

  const [order, setOrder] = useState<S['Order'] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ErrorDisplay | null>(null);

  function load() {
    if (!orderId) return;
    setLoading(true);
    setError(null);
    api
      .getOrder(browserApi(), orderId)
      .then(setOrder)
      .catch((cause: unknown) => setError(describeError(cause)))
      .finally(() => setLoading(false));
  }

  useEffect(load, [orderId]);

  if (!orderId) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <ErrorState title="缺少訂單編號，無法查詢付款結果。" />
      </main>
    );
  }

  if (loading) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <Skeleton variant="block" className="h-48 w-full" />
      </main>
    );
  }

  if (error) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <ErrorState title={error.title} traceId={error.traceId} onRetry={load} />
      </main>
    );
  }

  if (!order) return null;

  const isPaid = PAID_STATUSES.has(order.status);
  const isCancelled = order.status === 'Cancelled';
  const isAwaitingPayment = order.status === 'AwaitingPayment';

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <Card padding="lg" className="flex flex-col items-center gap-[var(--gg-space-3)] text-center">
        {isPaid && (
          <>
            <h1 className="font-display text-[length:var(--gg-text-xl)] font-bold text-success">付款成功</h1>
            <p className="text-fg-muted">訂單 {order.orderNumber} 已收到您的付款，我們會盡快為您安排。</p>
          </>
        )}
        {isCancelled && (
          <>
            <h1 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">訂單已取消</h1>
            <p className="text-fg-muted">訂單 {order.orderNumber} 已取消，如已扣款會依原路退還。</p>
          </>
        )}
        {isAwaitingPayment && (
          <>
            <h1 className="font-display text-[length:var(--gg-text-xl)] font-bold text-warning-text">尚未確認付款</h1>
            <p className="text-fg-muted">
              還沒收到訂單 {order.orderNumber} 的付款確認，可能還在處理中，或是剛剛付款沒有完成。
              {order.paymentDueAt && `逾期未付款會自動取消，付款期限：${new Date(order.paymentDueAt).toLocaleString('zh-TW')}。`}
            </p>
          </>
        )}

        <PriceDisplay amount={order.grandTotal} size="lg" />
        <ExplainDisclosure items={order.quoteExplain ?? []} title="這筆金額怎麼算的？" />

        <div className="flex flex-wrap items-center justify-center gap-[var(--gg-space-3)]">
          {isAwaitingPayment && (
            <Link href={`/payment/${order.id}`}>
              <Button variant="primary">重新前往付款</Button>
            </Link>
          )}
          <Link href="/orders">
            <Button variant="secondary">查看我的訂單</Button>
          </Link>
        </div>
      </Card>
    </main>
  );
}
