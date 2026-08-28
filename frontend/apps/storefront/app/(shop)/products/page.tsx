import type { Metadata } from 'next';
import { listProducts, type ListProductsQuery } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { serverApi } from '../../_lib/apiClient';
import { InfiniteProductGrid } from '../_components/InfiniteProductGrid';
import { ProductFilters } from './_components/ProductFilters';

type S = components['schemas'];

interface ProductsPageProps {
  searchParams: Promise<{ q?: string; mode?: string; categoryId?: string }>;
}

export const metadata: Metadata = { title: '商品列表' };

function parseMode(value: string | undefined): S['FulfillmentMode'] | undefined {
  return value === 'Stock' || value === 'Preorder' ? value : undefined;
}

export default async function ProductsPage({ searchParams }: ProductsPageProps) {
  const params = await searchParams;
  const q = params.q ?? '';
  const mode = parseMode(params.mode);
  const categoryId = params.categoryId;

  // exactOptionalPropertyTypes：key 存在就不能是 undefined，篩選條件用展開式視情況加進去。
  const query: Omit<ListProductsQuery, 'cursor' | 'limit'> = {
    ...(q ? { q } : {}),
    ...(mode ? { mode } : {}),
    ...(categoryId ? { categoryId } : {}),
  };

  const api = await serverApi();
  const firstPage = await listProducts(api, { ...query, limit: 20 });

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)]">
      <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">商品列表</h1>

      <ProductFilters initialQuery={q} initialMode={mode ?? 'all'} />

      <InfiniteProductGrid
        initialItems={firstPage.items}
        initialCursor={firstPage.nextCursor}
        query={query}
        emptyTitle={q ? `找不到「${q}」的商品` : '目前沒有符合條件的商品'}
        emptyDescription="換個關鍵字或篩選條件試試看。"
      />
    </main>
  );
}
