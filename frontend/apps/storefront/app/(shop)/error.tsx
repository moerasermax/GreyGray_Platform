'use client';

import { useEffect } from 'react';
import { ErrorState } from '@greygray/ui';
import { ApiError } from '@greygray/api-client';

/** `(shop)/**` 共用的錯誤畫面。伺服器端擲出的 `ApiError` 會帶著 problem 的標題與 traceId。 */
export default function ShopError({ error, reset }: { error: Error; reset: () => void }) {
  useEffect(() => {
    // eslint-disable-next-line no-console
    console.error(error);
  }, [error]);

  const title = error instanceof ApiError ? error.problem.title : '頁面發生問題，請稍後再試。';
  const traceId = error instanceof ApiError ? error.problem.traceId : null;

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <ErrorState title={title} traceId={traceId} onRetry={reset} />
    </main>
  );
}
