import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { Badge, Thumbnail } from '@greygray/ui';
import { getProduct } from '@greygray/api-client/endpoints/storefront';
import { ApiError } from '@greygray/api-client';
import { PageTopBar } from '../../../_components/PageTopBar';
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
    <>
      {/*
        #32：這一頁的分頁列被 `AddToCartPanel` 的 `BottomActionBar` 擠掉了，
        在頂部列出現之前畫面上一個出口都沒有。標題用商品名——只有這一頁知道它。
        擺在 `<main>` 外面是因為 `sticky top-0` 要吃整個視窗寬，
        放進 `<main>` 會被它的左右內距切掉一截。
      */}
      <PageTopBar title={product.name} />
      {/*
        桌面（lg 以上）兩欄：左媒體、右資訊。全寬的正方形媒體在 1440px 上會把商品名與加購
        整個推出首屏。手機維持單欄；中間寬度（平板）媒體限寬置中，不讓一張正方形圖吃掉整屏。
      */}
      <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)] pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-6))] lg:grid lg:grid-cols-2 lg:items-start lg:gap-[var(--gg-space-8)] lg:py-[var(--gg-space-6)] lg:pb-[calc(var(--gg-bottom-bar-height)+var(--gg-space-8))]">
        <div className="gg-square-media relative mx-auto flex w-full max-w-md items-center justify-center overflow-hidden rounded-[var(--gg-radius-xl)] border border-border-soft bg-surface-sunken lg:max-w-none">
          {/* 沒圖時 `Thumbnail` 什麼都不畫：放一個中性圖形與明講的提示，不捏造商品圖。 */}
          {!product.images[0] && (
            <div className="flex flex-col items-center gap-[var(--gg-space-3)] p-[var(--gg-space-4)] text-center">
              <span
                aria-hidden
                className="h-16 w-16 rounded-[var(--gg-radius-xl)] border border-dashed border-border-soft bg-surface"
              />
              <p className="text-[length:var(--gg-text-sm)] text-fg-muted">這件商品尚未提供圖片</p>
            </div>
          )}
          <Thumbnail src={product.images[0]} alt={product.name} sizes="(max-width: 1024px) 100vw, 600px" />
          {product.mode === 'Preorder' && (
            <div className="absolute left-[var(--gg-space-3)] top-[var(--gg-space-3)]">
              <Badge variant="Preorder" label="預購" />
            </div>
          )}
        </div>

        <div className="flex min-w-0 flex-col gap-[var(--gg-space-5)]">
          <header className="flex min-w-0 flex-col gap-[var(--gg-space-3)]">
            <h1 className="break-words font-display text-[length:var(--gg-text-2xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
              {product.name}
            </h1>
            {product.shortDescription && (
              <p className="break-words text-[length:var(--gg-text-base)] text-fg-muted">
                {product.shortDescription}
              </p>
            )}
            {product.description && (
              <p className="whitespace-pre-line break-words border-t border-border-soft pt-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] leading-[var(--gg-leading-normal)] text-fg">
                {product.description}
              </p>
            )}
          </header>

          {product.mode === 'Preorder' && product.campaign && (
            <Link
              href={`/campaigns/${product.campaign.id}`}
              className="flex min-w-0 flex-col gap-[var(--gg-space-1)] rounded-card bg-primary-subtle p-[var(--gg-space-3)]"
            >
              <span className="break-words text-[length:var(--gg-text-sm)] font-bold text-primary-text">
                這是預購商品，隨團出貨：{product.campaign.title}
              </span>
              <span className="break-words text-[length:var(--gg-text-xs)] text-fg-muted">
                {product.campaign.destination}．查看開團詳情
              </span>
            </Link>
          )}

          <AddToCartPanel product={product} />
        </div>
      </main>
    </>
  );
}
