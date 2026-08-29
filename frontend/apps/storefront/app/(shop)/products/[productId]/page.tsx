import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { Badge, Thumbnail } from '@greygray/ui';
import { getProduct } from '@greygray/api-client/endpoints/storefront';
import { ApiError } from '@greygray/api-client';
import { serverApi } from '../../../_lib/apiClient';
import { AddToCartPanel } from './_components/AddToCartPanel';

interface ProductDetailPageProps {
  params: Promise<{ productId: string }>;
}

async function loadProduct(productId: string) {
  const api = await serverApi();
  try {
    return await getProduct(api, productId);
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) notFound();
    throw cause;
  }
}

export async function generateMetadata({ params }: ProductDetailPageProps): Promise<Metadata> {
  const { productId } = await params;
  const product = await loadProduct(productId);
  const description = product.shortDescription ?? product.description ?? undefined;
  return {
    title: product.name,
    description,
    openGraph: {
      title: product.name,
      description,
      images: product.images[0] ? [product.images[0]] : undefined,
    },
  };
}

/**
 * 商品詳情走 SSR：`view-source` 要看得到商品名與描述（FE-3 驗收條件）。
 * 互動的部分（收藏、選規格、加購）都切到 `AddToCartPanel` 這個 client component。
 */
export default async function ProductDetailPage({ params }: ProductDetailPageProps) {
  const { productId } = await params;
  const product = await loadProduct(productId);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)] pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-6))]">
      <div className="gg-square-media relative w-full overflow-hidden rounded-[var(--gg-radius-xl)] bg-surface-sunken">
        <Thumbnail src={product.images[0]} alt={product.name} sizes="(max-width: 640px) 100vw, 640px" />
        {product.mode === 'Preorder' && (
          <div className="absolute left-[var(--gg-space-3)] top-[var(--gg-space-3)]">
            <Badge variant="Preorder" label="預購" />
          </div>
        )}
      </div>

      <header className="flex flex-col gap-[var(--gg-space-2)]">
        <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">
          {product.name}
        </h1>
        {product.shortDescription && (
          <p className="text-[length:var(--gg-text-base)] text-fg-muted">{product.shortDescription}</p>
        )}
        {product.description && (
          <p className="text-[length:var(--gg-text-sm)] leading-[var(--gg-leading-normal)] text-fg">
            {product.description}
          </p>
        )}
      </header>

      {product.mode === 'Preorder' && product.campaign && (
        <Link
          href={`/campaigns/${product.campaign.id}`}
          className="flex flex-col gap-[var(--gg-space-1)] rounded-card bg-primary-subtle p-[var(--gg-space-3)]"
        >
          <span className="text-[length:var(--gg-text-sm)] font-bold text-primary-text">
            這是預購商品，隨團出貨：{product.campaign.title}
          </span>
          <span className="text-[length:var(--gg-text-xs)] text-fg-muted">
            {product.campaign.destination}．查看開團詳情
          </span>
        </Link>
      )}

      <AddToCartPanel product={product} />
    </main>
  );
}
