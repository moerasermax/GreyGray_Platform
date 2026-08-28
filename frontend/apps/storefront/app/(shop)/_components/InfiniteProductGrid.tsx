'use client';

import { useCallback, useState } from 'react';
import { EmptyState, ErrorState, Skeleton } from '@greygray/ui';
import { listProducts, type ListProductsQuery } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { ApiError } from '@greygray/api-client';
import { browserApi } from '../../_lib/apiClient';
import { useInfiniteScrollSentinel } from '../_lib/useInfiniteScrollSentinel';
import { ProductCardLink } from './ProductCardLink';

type S = components['schemas'];

export interface InfiniteProductGridProps {
  initialItems: S['ProductListItem'][];
  initialCursor: string | null;
  /** 除了 cursor／limit 以外，篩選條件在第一頁與後續分頁都要一致。 */
  query: Omit<ListProductsQuery, 'cursor' | 'limit'>;
  emptyTitle: string;
  emptyDescription?: string;
}

/**
 * 游標式分頁的商品卡牆。`nextCursor` 為 `null` 時不再掛捲動偵測，
 * 保證最後一頁之後不會再打 API（FE-3 驗收條件之一）。
 */
export function InfiniteProductGrid({
  initialItems,
  initialCursor,
  query,
  emptyTitle,
  emptyDescription,
}: InfiniteProductGridProps) {
  const [items, setItems] = useState(initialItems);
  const [cursor, setCursor] = useState(initialCursor);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const loadMore = useCallback(() => {
    if (!cursor || loading) return;
    setLoading(true);
    setError(null);
    listProducts(browserApi(), { ...query, cursor, limit: 20 })
      .then((page) => {
        setItems((prev) => [...prev, ...page.items]);
        setCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        setError(cause instanceof ApiError ? cause : null);
        if (!(cause instanceof ApiError)) throw cause;
      })
      .finally(() => setLoading(false));
  }, [cursor, loading, query]);

  const sentinelRef = useInfiniteScrollSentinel(loadMore, cursor !== null && !error);

  if (items.length === 0 && !loading) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-5)]">
      <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
        {items.map((product) => (
          <ProductCardLink key={product.id} product={product} />
        ))}
      </div>

      {loading && (
        <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
          {Array.from({ length: 4 }).map((_, index) => (
            // eslint-disable-next-line react/no-array-index-key
            <Skeleton key={index} variant="block" className="aspect-square w-full" />
          ))}
        </div>
      )}

      {error && (
        <ErrorState
          title={error.problem.title}
          traceId={error.problem.traceId}
          onRetry={loadMore}
        />
      )}

      {/* 只要 cursor 還在就保留這個 sentinel；為 null 時整個 div 消失，observer 也不會掛上去。 */}
      {cursor !== null && !error && <div ref={sentinelRef} aria-hidden className="h-px w-full" />}
    </div>
  );
}
