import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { listCategories, listProducts } from '@greygray/api-client/endpoints/storefront';
import { serverApi } from '../../../_lib/apiClient';
import { getCategoryPageData } from '../../../_lib/categoryTree';
import { InfiniteProductGrid } from '../../_components/InfiniteProductGrid';

interface CategoryPageProps {
  params: Promise<{ categoryId: string }>;
}

/**
 * 契約沒有「單一分類」的端點，只有 `listCategories`。分類名稱要在清單裡找，
 * 找不到就是分類不存在（或已下架），直接 404。
 */
async function findCategory(categoryId: string) {
  const api = await serverApi();
  const categories = await listCategories(api);
  return categories.find((c) => c.id === categoryId) ?? null;
}

export async function generateMetadata({ params }: CategoryPageProps): Promise<Metadata> {
  const { categoryId } = await params;
  const category = await findCategory(categoryId);
  if (!category) return {};
  return {
    title: category.name,
    description: `${category.name}分類的所有商品`,
  };
}

export default async function CategoryPage({ params }: CategoryPageProps) {
  const { categoryId } = await params;
  const api = await serverApi();
  const categories = await listCategories(api);
  const pageData = getCategoryPageData(categories, categoryId);
  if (!pageData) notFound();
  const { category, parent, children, productQuery } = pageData;
  const firstPage = await listProducts(api, { ...productQuery, limit: 20 });

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)]">
      {parent ? (
        <Link
          href={`/categories/${parent.id}`}
          className="flex min-h-[var(--gg-touch-min)] w-fit items-center rounded-pill px-[var(--gg-space-3)] font-bold text-primary-text no-underline hover:bg-primary-subtle"
        >
          ← {parent.name}
        </Link>
      ) : null}
      <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">
        {category.name}
      </h1>

      {children.length > 0 ? (
        <nav aria-label={`${category.name}的子分類`} className="flex flex-wrap gap-[var(--gg-space-2)]">
          {children.map((child) => (
            <Link
              key={child.id}
              href={`/categories/${child.id}`}
              className="flex min-h-[var(--gg-touch-min)] items-center rounded-pill border border-border-soft px-[var(--gg-space-4)] font-bold text-fg no-underline hover:bg-surface-sunken"
            >
              {child.name}
            </Link>
          ))}
        </nav>
      ) : null}

      <InfiniteProductGrid
        initialItems={firstPage.items}
        initialCursor={firstPage.nextCursor}
        query={productQuery}
        emptyTitle="這個分類目前沒有商品"
        emptyDescription="晚點再回來看看，或去逛逛其他分類。"
      />
    </main>
  );
}
