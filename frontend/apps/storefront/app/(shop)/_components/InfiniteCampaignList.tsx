'use client';

import { useCallback, useState } from 'react';
import { EmptyState, ErrorState, Skeleton } from '@greygray/ui';
import { listCampaigns } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { ApiError } from '@greygray/api-client';
import { browserApi } from '../../_lib/apiClient';
import { useInfiniteScrollSentinel } from '../_lib/useInfiniteScrollSentinel';
import { CampaignCard } from './CampaignCard';

type S = components['schemas'];

export interface InfiniteCampaignListProps {
  initialItems: S['CampaignListItem'][];
  initialCursor: string | null;
  status: S['CampaignStatus'];
}

/** 開團列表。同 `InfiniteProductGrid` 的規則：`nextCursor` 為 `null` 就不再打 API。 */
export function InfiniteCampaignList({ initialItems, initialCursor, status }: InfiniteCampaignListProps) {
  const [items, setItems] = useState(initialItems);
  const [cursor, setCursor] = useState(initialCursor);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const loadMore = useCallback(() => {
    if (!cursor || loading) return;
    setLoading(true);
    setError(null);
    listCampaigns(browserApi(), { status, cursor, limit: 10 })
      .then((page) => {
        setItems((prev) => [...prev, ...page.items]);
        setCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        setError(cause instanceof ApiError ? cause : null);
        if (!(cause instanceof ApiError)) throw cause;
      })
      .finally(() => setLoading(false));
  }, [cursor, loading, status]);

  const sentinelRef = useInfiniteScrollSentinel(loadMore, cursor !== null && !error);

  if (items.length === 0 && !loading) {
    return <EmptyState title="目前沒有開團中的旅程" description="晚點再回來看看，或先逛逛現貨商品。" />;
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-5)]">
      <div className="grid grid-cols-1 gap-[var(--gg-space-4)] sm:grid-cols-2">
        {items.map((campaign) => (
          <CampaignCard key={campaign.id} campaign={campaign} />
        ))}
      </div>

      {loading && (
        <div className="grid grid-cols-1 gap-[var(--gg-space-4)] sm:grid-cols-2">
          {Array.from({ length: 2 }).map((_, index) => (
            // eslint-disable-next-line react/no-array-index-key
            <Skeleton key={index} variant="block" className="aspect-[4/3] w-full" />
          ))}
        </div>
      )}

      {error && (
        <ErrorState title={error.problem.title} traceId={error.problem.traceId} onRetry={loadMore} />
      )}

      {cursor !== null && !error && <div ref={sentinelRef} aria-hidden className="h-px w-full" />}
    </div>
  );
}
