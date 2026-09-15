'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Card, FavoriteHeart, ProductCard, Toast, suppressCardNavigation } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { loginHrefForCurrentPage } from '../../(account)/_lib/authRedirect';
import { useFavoriteToggle } from '../../_lib/favorites';
import { toBadgeProps } from '../_lib/badges';

type S = components['schemas'];

/**
 * 商品卡 ＋ 導頁 ＋ 收藏心。收藏切換與商品頁共用 `_lib/favorites.ts` 的狀態機。
 */
export function ProductCardLink({ product }: { product: S['ProductListItem'] }) {
  const router = useRouter();
  const favorite = useFavoriteToggle({
    productId: product.id,
    initialFavorited: product.isFavorited,
    onUnauthorized: () => router.replace(loginHrefForCurrentPage()),
  });

  const card = product.priceFrom ? (
    <ProductCard
      imageSrc={product.imageUrl}
      imageAlt={product.name}
      name={product.name}
      description={product.shortDescription ?? undefined}
      price={product.priceFrom}
      unitPriceLabel={product.unitPriceLabel ?? undefined}
      badges={toBadgeProps(product.badges)}
      favorited={favorite.favorited}
      onToggleFavorite={() => void favorite.toggle()}
      className="h-full"
    />
  ) : (
      // 契約允許未定價商品出現在最愛清單；這個分支也必須能取消收藏。
      <Card className="relative flex h-full flex-col gap-[var(--gg-space-2)]">
        <span
          className="absolute right-[var(--gg-space-2)] top-[var(--gg-space-2)]"
          onClick={suppressCardNavigation}
        >
          <FavoriteHeart
            pressed={favorite.favorited}
            onToggle={() => void favorite.toggle()}
            aria-label={favorite.favorited ? `取消收藏 ${product.name}` : `加入收藏 ${product.name}`}
          />
        </span>
        <p className="line-clamp-1 pr-[var(--gg-space-8)] font-display font-bold text-fg">{product.name}</p>
        {product.shortDescription && (
          <p className="line-clamp-2 text-[length:var(--gg-text-sm)] text-fg-muted">
            {product.shortDescription}
          </p>
        )}
        <p className="mt-auto text-[length:var(--gg-text-xs)] text-fg-muted">
          目前無法購買，點擊查看詳情
        </p>
      </Card>
  );

  return (
    <>
      <Link href={`/products/${product.id}`} className="block h-full" prefetch={false}>
        {card}
      </Link>
      <div className="fixed inset-x-[var(--gg-space-4)] bottom-[calc(var(--gg-bottom-bar-height)+var(--gg-space-3))] z-[var(--gg-z-modal)] mx-auto max-w-sm">
        <Toast
          open={favorite.errorMessage !== null}
          variant="error"
          message={favorite.errorMessage ?? ''}
          onClose={favorite.clearError}
          duration={4000}
        />
      </div>
    </>
  );
}
