'use client';

import * as storefrontApi from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { Button, Card, EmptyState, ErrorState, PriceDisplay, Select, Skeleton } from '@greygray/ui';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { isUnauthorized, loginHrefForCurrentPage } from '../_lib/authRedirect';
import { generalErrorMessage, traceIdOf } from '../_lib/formErrors';
import { ORDER_MAIN_LINE, orderStatusLabel } from '../_lib/orderStatus';

type OrderListItem = components['schemas']['OrderListItem'];
type OrderStatus = components['schemas']['OrderStatus'];

const STATUS_FILTER_OPTIONS: Array<{ value: '' | OrderStatus; label: string }> = [
  { value: '', label: '全部訂單' },
  ...ORDER_MAIN_LINE.map((status) => ({ value: status, label: orderStatusLabel(status) })),
  { value: 'Cancelled', label: orderStatusLabel('Cancelled') },
];

const PAGE_SIZE = 20;

export default function OrdersPage() {
  const router = useRouter();
  const [statusFilter, setStatusFilter] = useState<'' | OrderStatus>('');
  const [items, setItems] = useState<OrderListItem[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const loadFirstPage = useCallback(
    (status: '' | OrderStatus) => {
      setLoading(true);
      setError(null);
      storefrontApi
        .listOrders(browserApi(), { ...(status ? { status } : {}), limit: PAGE_SIZE })
        .then((page) => {
          setItems(page.items);
          setNextCursor(page.nextCursor);
        })
        .catch((caught: unknown) => {
          if (isUnauthorized(caught)) {
            router.replace(loginHrefForCurrentPage());
            return;
          }
          setError(caught);
        })
        .finally(() => setLoading(false));
    },
    [router],
  );

  useEffect(() => {
    loadFirstPage(statusFilter);
  }, [statusFilter, loadFirstPage]);

  async function loadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await storefrontApi.listOrders(browserApi(), {
        ...(statusFilter ? { status: statusFilter } : {}),
        cursor: nextCursor,
        limit: PAGE_SIZE,
      });
      setItems((prev) => [...prev, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (caught) {
      if (isUnauthorized(caught)) {
        router.replace(loginHrefForCurrentPage());
        return;
      }
      setError(caught);
    } finally {
      setLoadingMore(false);
    }
  }

  return (
    <main className="mx-auto flex max-w-[640px] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <header className="flex flex-col gap-[var(--gg-space-3)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">我的訂單</h1>
        <label className="flex flex-col gap-[var(--gg-space-1)]">
          <span className="text-[length:var(--gg-text-sm)] font-bold text-fg">狀態篩選</span>
          <Select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as '' | OrderStatus)}
          >
            {STATUS_FILTER_OPTIONS.map((option) => (
              <option key={option.value || 'all'} value={option.value}>
                {option.label}
              </option>
            ))}
          </Select>
        </label>
      </header>

      {loading && (
        <div className="flex flex-col gap-[var(--gg-space-3)]">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} variant="block" className="h-[96px] w-full" />
          ))}
        </div>
      )}

      {!loading && error != null && (
        <Card padding="none">
          <ErrorState
            title={generalErrorMessage(error)}
            traceId={traceIdOf(error)}
            onRetry={() => loadFirstPage(statusFilter)}
          />
        </Card>
      )}

      {!loading && !error && items.length === 0 && (
        <Card padding="none">
          <EmptyState
            title="還沒有任何訂單"
            description="逛逛商品或開團，喜歡的先放進購物車。"
            action={
              <Link href="/">
                <Button size="sm">去逛逛</Button>
              </Link>
            }
          />
        </Card>
      )}

      {!loading && !error && items.length > 0 && (
        <div className="flex flex-col gap-[var(--gg-space-3)]">
          {items.map((order) => (
            /*
             * 手機：識別資訊與金額上下排，金額自己一行靠右——兩邊都拿得到整個寬度，
             * 不會互擠。`sm` 以上才並排，這時左欄 `min-w-0` 可收縮換行、金額 `shrink-0` 保持完整。
             * （只加 `shrink-0` 不改手機排法的話，大金額會把自己推出卡片。）
             * `block rounded-card` 的理由同 `me/page.tsx`：焦點框要跟著整張卡的圓角。
             */
            <Link key={order.id} href={`/orders/${order.id}`} className="block rounded-card">
              <Card className="flex flex-col gap-[var(--gg-space-2)] transition-colors duration-[var(--gg-duration-fast)] ease-out-soft hover:bg-surface-sunken sm:flex-row sm:items-center sm:justify-between sm:gap-[var(--gg-space-4)]">
                <div className="flex min-w-0 flex-col gap-[var(--gg-space-1)]">
                  <p className="font-bold text-fg [overflow-wrap:anywhere]">{order.orderNumber}</p>
                  <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
                    {orderStatusLabel(order.status)}・{order.lineCount} 項商品
                  </p>
                  <p className="text-[length:var(--gg-text-xs)] text-fg-muted">
                    {new Date(order.placedAt).toLocaleString('zh-TW')}
                  </p>
                </div>
                <PriceDisplay amount={order.grandTotal} className="max-w-full self-end sm:shrink-0 sm:self-auto" />
              </Card>
            </Link>
          ))}

          {nextCursor && (
            <Button variant="secondary" onClick={loadMore} loading={loadingMore}>
              載入更多
            </Button>
          )}
        </div>
      )}
    </main>
  );
}
