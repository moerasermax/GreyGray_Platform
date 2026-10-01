'use client';

/*
 * 付款導轉頁。docs/06 FE-4 最直白的一條規則：
 * **拿 `PaymentInitiation.fields` 建一個 hidden form，原封不動 POST 到 `action`。**
 * 不組簽章、不改欄位值、不改欄位順序、不少送欄位——綠界會驗簽，改了就失敗。
 *
 * `initiatePayment` 是寫入端點（要冪等鍵），用 payload-aware action 包起來，
 * 防住 React Strict Mode 的 effect 重複執行，也避免同元件切到另一張訂單時沿用舊 key。
 */

import Link from 'next/link';
import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Button, ErrorState, Spinner } from '@greygray/ui';
import { ApiError } from '@greygray/api-client';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../../_lib/apiClient';
import { loginHref } from '../../../_lib/auth';
import { createPayloadIdempotentAction } from '../../_lib/idempotentAction';
import { describeError, type ErrorDisplay } from '../../_lib/errorDisplay';
import {
  paymentInitiationFailureAction,
  paymentConflictRefreshAction,
  paymentOrderLoadFailureAction,
  paymentRedirectAction,
} from '../../_lib/paymentInstructions';
import { PaymentInstructionsCard } from '../../../(account)/orders/_components/PaymentInstructionsCard';
import { CancellationNotice, PaymentOverdueNotice } from '../../_lib/orderPaymentNotices';

type S = components['schemas'];

export default function PaymentRedirectPage() {
  const params = useParams<{ orderId: string }>();
  const orderId = params.orderId;
  const router = useRouter();

  const [initiation, setInitiation] = useState<S['PaymentInitiation'] | null>(null);
  const [order, setOrder] = useState<S['Order'] | null>(null);
  const [error, setError] = useState<ErrorDisplay | null>(null);
  const formRef = useRef<HTMLFormElement>(null);

  const actionRef = useRef<ReturnType<typeof createPayloadIdempotentAction<string, S['PaymentInitiation']>> | null>(null);
  actionRef.current ??= createPayloadIdempotentAction((requestedOrderId, idempotencyKey) =>
    api.initiatePayment(browserApi(), requestedOrderId, { idempotencyKey }),
  );

  function goToLogin() {
    router.push(loginHref(`/payment/${orderId}`));
  }

  function requestPayment() {
    setError(null);
    setInitiation(null);
    actionRef.current!.run(orderId).then(setInitiation).catch((cause: unknown) => {
      /*
       * 初始化付款需要登入。session 過期時原本只會顯示一個 problem title，
       * 而這一頁**沒有任何出口**（`topBar.ts` 的 `SHELL_EXCEPTIONS` 刻意兩種殼都不放），
       * 人就真的卡在這裡。回程指回這一頁：登入完直接繼續付這張訂單。
       */
      const action = paymentInitiationFailureAction(cause);
      if (action === 'login') return goToLogin();
      if (action === 'reload-order') {
        api
          .getOrder(browserApi(), orderId)
          .then((refreshed) => {
            const refreshedAction = paymentConflictRefreshAction(refreshed);
            if (refreshed.status === 'Cancelled' || refreshedAction === 'show-instructions' || refreshedAction === 'show-overdue') {
              setOrder(refreshed);
            }
            else setError(describeError(cause));
          })
          .catch((reloadCause: unknown) => {
            if (reloadCause instanceof ApiError && reloadCause.isUnauthorized) return goToLogin();
            setError(describeError(reloadCause));
          });
        return;
      }
      setError(describeError(cause));
    });
  }

  function loadOrderBeforePayment() {
    setError(null);
    setInitiation(null);
    setOrder(null);
    api
      .getOrder(browserApi(), orderId)
      .then((loadedOrder) => {
        const redirectAction = paymentRedirectAction(loadedOrder);
        if (loadedOrder.status === 'Cancelled' || redirectAction === 'show-instructions' || redirectAction === 'show-overdue') {
          setOrder(loadedOrder);
        }
        else requestPayment();
      })
      .catch((cause: unknown) => {
        if (paymentOrderLoadFailureAction(cause) === 'login') goToLogin();
        else requestPayment();
      });
  }

  useEffect(() => {
    loadOrderBeforePayment();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId]);

  useEffect(() => {
    if (initiation) formRef.current?.submit();
  }, [initiation]);

  if (order?.status === 'Cancelled') {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <h1 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">訂單已取消</h1>
        <CancellationNotice order={order} />
        <div className="flex flex-col gap-[var(--gg-space-3)] sm:flex-row sm:justify-center">
          <Link href={`/orders/${orderId}`}><Button variant="secondary" className="w-full sm:w-auto">查看訂單明細</Button></Link>
          <Link href="/"><Button variant="secondary" className="w-full sm:w-auto">回首頁</Button></Link>
        </div>
      </main>
    );
  }

  if (order?.paymentOverdue === true) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <PaymentOverdueNotice />
        <div className="flex flex-col gap-[var(--gg-space-3)] sm:flex-row sm:justify-center">
          <Link href={`/orders/${orderId}`}><Button variant="secondary" className="w-full sm:w-auto">查看訂單明細</Button></Link>
          <Link href="/"><Button variant="secondary" className="w-full sm:w-auto">回首頁</Button></Link>
        </div>
      </main>
    );
  }

  if (order?.paymentInstructions != null) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <PaymentInstructionsCard order={order} />
        <div className="flex flex-col gap-[var(--gg-space-3)] sm:flex-row sm:justify-center">
          <Link href={`/orders/${orderId}`}>
            <Button variant="secondary" className="w-full sm:w-auto">
              查看訂單明細
            </Button>
          </Link>
          <Link href="/">
            <Button variant="secondary" className="w-full sm:w-auto">
              回首頁
            </Button>
          </Link>
        </div>
      </main>
    );
  }

  if (error) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <ErrorState title={error.title} traceId={error.traceId} onRetry={loadOrderBeforePayment} />
        {/*
         * FE-46：這一頁依 `TAB_BAR_RULES` 刻意不畫分頁列與頁首，所以錯誤狀態原本只有「重試」，
         * 沒有任何出口（#30／#32 同型：不顯示導覽就要給替代出口）。
         * **只在錯誤狀態加**；正在導轉綠界的狀態不加任何可點的東西，那是刻意的。
         */}
        <div className="flex flex-col gap-[var(--gg-space-3)] sm:flex-row sm:justify-center">
          <Link href="/orders">
            <Button variant="secondary" className="w-full sm:w-auto">
              查看我的訂單
            </Button>
          </Link>
          <Link href="/">
            <Button variant="secondary" className="w-full sm:w-auto">
              回首頁
            </Button>
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col items-center gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)] text-center">
      <Spinner label="正在導向付款頁面" className="text-[length:var(--gg-text-3xl)] text-primary-text" />
      <p className="text-fg-muted">正在導向綠界付款頁面，請稍候……</p>

      {initiation && (
        <>
          <form ref={formRef} method={initiation.method} action={initiation.action} className="hidden">
            {Object.entries(initiation.fields).map(([name, value]) => (
              <input key={name} type="hidden" name={name} value={value} />
            ))}
          </form>
          <Button variant="secondary" onClick={() => formRef.current?.submit()}>
            如果沒有自動跳轉，請點這裡
          </Button>
        </>
      )}
    </main>
  );
}
