'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Card, EmptyState, ErrorState, Skeleton } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { listFavorites } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from '../../_lib/apiClient';
import { ProductCardLink } from '../../(shop)/_components/ProductCardLink';
import { generalErrorMessage, traceIdOf } from '../_lib/formErrors';
import {
  appendFavoritePage,
  canLoadMore,
  favoriteListLoginHref,
  favoriteListView,
} from './listState';

type Product = components['schemas']['ProductListItem'];
const PAGE_SIZE = 20;

export default function FavoritesPage() {
  const router = useRouter();
  const [items, setItems] = useState<Product[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const loadingMoreRef = useRef(false);

  const loadFirstPage = useCallback(() => {
    setLoading(true);
    setError(null);
    listFavorites(browserApi(), { limit: PAGE_SIZE })
      .then((page) => {
        setItems(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((caught: unknown) => {
        const login = favoriteListLoginHref(caught);
        if (login) {
          router.replace(login);
          return;
        }
        setError(caught);
      })
      .finally(() => setLoading(false));
  }, [router]);

  useEffect(() => {
    loadFirstPage();
  }, [loadFirstPage]);

  async function loadMore() {
    if (!canLoadMore(nextCursor, loadingMoreRef.current)) return;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    setError(null);
    try {
      const page = await listFavorites(browserApi(), { cursor: nextCursor, limit: PAGE_SIZE });
      setItems((current) => appendFavoritePage(current, page.items));
      setNextCursor(page.nextCursor);
    } catch (caught) {
      const login = favoriteListLoginHref(caught);
      if (login) {
        router.replace(login);
        return;
      }
      setError(caught);
    } finally {
      loadingMoreRef.current = false;
      setLoadingMore(false);
    }
  }

  const view = favoriteListView(loading, error, items);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">我的最愛</h1>

      {view === 'loading' && (
        <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
          {[0, 1, 2, 3].map((index) => (
            <Skeleton key={index} variant="block" className="aspect-square w-full" />
          ))}
        </div>
      )}

      {view === 'error' && (
        <Card padding="none">
          <ErrorState
            title={generalErrorMessage(error)}
            traceId={traceIdOf(error)}
            onRetry={loadFirstPage}
          />
        </Card>
      )}

      {view === 'empty' && (
        <Card padding="none">
          <EmptyState
            title="還沒有收藏的商品"
            description="看到喜歡的商品時，按愛心就能收進這裡。"
            action={
              <Link href="/">
                <Button size="sm">回首頁逛逛</Button>
              </Link>
            }
          />
        </Card>
      )}

      {view === 'items' && (
        <div className="flex flex-col gap-[var(--gg-space-5)]">
          <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
            {items.map((product) => (
              <ProductCardLink key={product.id} product={product} />
            ))}
          </div>
          {canLoadMore(nextCursor, false) && (
            <Button variant="secondary" onClick={() => void loadMore()} loading={loadingMore}>
              載入更多
            </Button>
          )}
        </div>
      )}
    </main>
  );
}
