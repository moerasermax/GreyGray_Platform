'use client';

/*
 * 付款導轉頁。docs/06 FE-4 最直白的一條規則：
 * **拿 `PaymentInitiation.fields` 建一個 hidden form，原封不動 POST 到 `action`。**
 * 不組簽章、不改欄位值、不改欄位順序、不少送欄位——綠界會驗簽，改了就失敗。
 *
 * `initiatePayment` 是寫入端點（要冪等鍵），用 payload-aware action 包起來，
 * 防住 React Strict Mode 的 effect 重複執行，也避免同元件切到另一張訂單時沿用舊 key。
 */

import { useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { Button, ErrorState, Spinner } from '@greygray/ui';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../../_lib/apiClient';
import { createPayloadIdempotentAction } from '../../_lib/idempotentAction';
import { describeError, type ErrorDisplay } from '../../_lib/errorDisplay';

type S = components['schemas'];

export default function PaymentRedirectPage() {
  const params = useParams<{ orderId: string }>();
  const orderId = params.orderId;

  const [initiation, setInitiation] = useState<S['PaymentInitiation'] | null>(null);
  const [error, setError] = useState<ErrorDisplay | null>(null);
  const formRef = useRef<HTMLFormElement>(null);

  const actionRef = useRef<ReturnType<typeof createPayloadIdempotentAction<string, S['PaymentInitiation']>> | null>(null);
  actionRef.current ??= createPayloadIdempotentAction((requestedOrderId, idempotencyKey) =>
    api.initiatePayment(browserApi(), requestedOrderId, { idempotencyKey }),
  );

  function requestPayment() {
    setError(null);
    setInitiation(null);
    actionRef.current!.run(orderId).then(setInitiation).catch((cause: unknown) => setError(describeError(cause)));
  }

  useEffect(() => {
    requestPayment();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId]);

  useEffect(() => {
    if (initiation) formRef.current?.submit();
  }, [initiation]);

  if (error) {
    return (
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-4)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
        <ErrorState title={error.title} traceId={error.traceId} onRetry={requestPayment} />
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
