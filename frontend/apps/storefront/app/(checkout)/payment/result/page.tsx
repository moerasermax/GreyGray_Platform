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
import { Suspense, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { Button, Card, ErrorState, PriceDisplay, Skeleton } from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../../_lib/apiClient';
import { ExplainDisclosure } from '../../_components/ExplainDisclosure';
import { describeError, type ErrorDisplay } from '../../_lib/errorDisplay';
import { pollDelayMs, shouldKeepPolling } from '../../_lib/paymentResultPolling';

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
  /*
   * 自動重查用的狀態。
   *
   * ── 為什麼要重查 ──
   * 綠界回呼是非同步的（回呼 → outbox → Worker），瀏覽器從收銀台回到這一頁時
   * 多半只過了一秒，訂單還停在 `AwaitingPayment`，畫面就會寫「尚未確認付款」。
   * 人剛刷完卡看到這五個字會以為失敗，然後去刷第二次。
   *
   * ── 為什麼有限次 ──
   * 排程與理由都在 `_lib/paymentResultPolling.ts`：付款真的失敗或 Worker 掛掉時，
   * 「輪詢到好為止」會變成一個永遠不結束、也永遠不給人下一步的畫面。
   * 排程用完就停下來，把「要不要再查」交還給使用者（下面那顆「重新查詢」）。
   */
  const [attempt, setAttempt] = useState(0);
  /**
   * 排程是不是已經跑完了。**顯示哪一段文案只看這一個旗標**，不看「現在有沒有排到計時器」——
   * 後者在第一次 render 與 effect 之間有一個空檔，會讓「尚未確認付款」閃一下才變成
   * 「正在確認」，而那一閃正是這一段要修掉的誤導。
   */
  const [pollExhausted, setPollExhausted] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function fetchOrder(): Promise<void> {
    if (!orderId) return Promise.resolve();
    setError(null);
    return api
      .getOrder(browserApi(), orderId)
      .then((result) => setOrder(result))
      .catch((cause: unknown) => setError(describeError(cause)));
  }

  /** 初次載入與使用者按「重試」／「重新查詢」時都走這裡：把重查排程整個重來。 */
  function load() {
    if (!orderId) return;
    setLoading(true);
    setAttempt(0);
    setPollExhausted(false);
    void fetchOrder().finally(() => setLoading(false));
  }

  useEffect(load, [orderId]);

  /*
   * 排下一次重查。依賴 `order`：每查回一次就重新評估一次要不要再排，
   * 狀態一變（付款成功、取消）條件就不成立，計時器不會再被排出去。
   * 清理函式保證離開頁面或重新排程時前一個計時器一定被取消。
   */
  useEffect(() => {
    if (loading || error || !order) return;
    if (!shouldKeepPolling(order.status, attempt)) {
      // 還是 AwaitingPayment 但排程用完了——停手，把下一步交給使用者。
      if (order.status === 'AwaitingPayment') setPollExhausted(true);
      return;
    }

    const delay = pollDelayMs(attempt);
    if (delay === null) return;

    timerRef.current = setTimeout(() => {
      void fetchOrder().finally(() => setAttempt((previous) => previous + 1));
    }, delay);

    return () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
      timerRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [order, attempt, loading, error]);

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
        {isAwaitingPayment && !pollExhausted && (
          <>
            <h1 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">正在確認付款</h1>
            <p className="text-fg-muted">
              正在向付款服務確認訂單 {order.orderNumber} 的付款結果，請稍候……
            </p>
          </>
        )}
        {isAwaitingPayment && pollExhausted && (
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
          {/*
            * 排程跑完、狀態仍是 AwaitingPayment 時才給「重新查詢」。
            * 重查期間給它只會讓人重複按，而每一次按都只是把同一個排程再跑一遍。
            */}
          {isAwaitingPayment && pollExhausted && (
            <Button variant="primary" onClick={load}>
              重新查詢
            </Button>
          )}
          {isAwaitingPayment && (
            <Link href={`/payment/${order.id}`}>
              <Button variant={pollExhausted ? 'secondary' : 'primary'}>重新前往付款</Button>
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
