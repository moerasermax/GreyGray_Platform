'use client';

import { useState } from 'react';
import Link from 'next/link';
import { Card, ProductCard } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { toBadgeProps } from '../_lib/badges';
import { placeholderImage } from '../_lib/placeholderImage';

type S = components['schemas'];

/**
 * 商品卡 ＋ 導頁 ＋ 收藏心。
 *
 * **收藏是純前端本地狀態，重新整理會消失。** 契約目前只有 `isFavorited` 這個
 * 讀取欄位，沒有對應的收藏／取消收藏端點——回報見交付清單「契約有問題的地方」。
 */
export function ProductCardLink({ product }: { product: S['ProductListItem'] }) {
  const [favorited, setFavorited] = useState(product.isFavorited);

  if (!product.priceFrom) {
    // 沒有可售 SKU 的邊界情況：目前 fixture 沒有這種商品，但契約允許 priceFrom 為 null。
    return (
      <Link href={`/products/${product.id}`} className="block h-full">
        <Card className="flex h-full flex-col gap-[var(--gg-space-2)]">
          <p className="line-clamp-1 font-display font-bold text-fg">{product.name}</p>
          {product.shortDescription && (
            <p className="line-clamp-2 text-[length:var(--gg-text-sm)] text-fg-muted">
              {product.shortDescription}
            </p>
          )}
          <p className="mt-auto text-[length:var(--gg-text-xs)] text-fg-muted">
            目前無法購買，點擊查看詳情
          </p>
        </Card>
      </Link>
    );
  }

  return (
    <Link href={`/products/${product.id}`} className="block h-full" prefetch={false}>
      <ProductCard
        imageSrc={product.imageUrl ?? placeholderImage(product.name)}
        imageAlt={product.name}
        name={product.name}
        description={product.shortDescription ?? undefined}
        price={product.priceFrom}
        unitPriceLabel={product.unitPriceLabel ?? undefined}
        badges={toBadgeProps(product.badges)}
        favorited={favorited}
        onToggleFavorite={() => setFavorited((value) => !value)}
        className="h-full"
      />
    </Link>
  );
}
