'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Card, ErrorState, PriceDisplay, Skeleton } from '@greygray/ui';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { isUnauthorized, loginHrefForCurrentPage } from '../_lib/authRedirect';
import { generalErrorMessage, traceIdOf } from '../_lib/formErrors';

type Money = components['schemas']['Money'];

export default function WalletPage() {
  const router = useRouter();
  const [balance, setBalance] = useState<Money | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const load = () => {
    setLoading(true);
    setError(null);
    storefrontApi
      .getStoredValueBalance(browserApi())
      .then((result) => setBalance(result.balance))
      .catch((caught: unknown) => {
        if (isUnauthorized(caught)) {
          router.replace(loginHrefForCurrentPage());
          return;
        }
        setError(caught);
      })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [router]);

  return (
    <main className="mx-auto flex max-w-[480px] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">儲值金餘額</h1>

      {loading && <Skeleton variant="block" className="h-[140px] w-full" />}

      {!loading && error != null && (
        <Card padding="none">
          <ErrorState title={generalErrorMessage(error)} traceId={traceIdOf(error)} onRetry={load} />
        </Card>
      )}

      {!loading && !error && balance && (
        <Card className="flex flex-col items-center gap-[var(--gg-space-3)] py-[var(--gg-space-7)] text-center">
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">目前餘額</p>
          <PriceDisplay amount={balance} size="lg" />
          <p className="max-w-xs text-[length:var(--gg-text-sm)] text-fg-muted">
            退款選儲值金零手續費，下次購物可以直接折抵。
          </p>
        </Card>
      )}
    </main>
  );
}
