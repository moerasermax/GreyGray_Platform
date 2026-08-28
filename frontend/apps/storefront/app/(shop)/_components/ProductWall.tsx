import Link from 'next/link';
import type { components } from '@greygray/api-client/storefront';
import { ProductCardLink } from './ProductCardLink';

type S = components['schemas'];

/** 商品卡牆（ADR-009 首頁切版第四段）。首頁只顯示第一批，其餘導去 `/products`。 */
export function ProductWall({ products }: { products: S['ProductListItem'][] }) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-4)]">
      <div className="grid grid-cols-2 gap-[var(--gg-space-4)] sm:grid-cols-3 md:grid-cols-4">
        {products.map((product) => (
          <ProductCardLink key={product.id} product={product} />
        ))}
      </div>
      <Link
        href="/products"
        className="self-center rounded-pill px-[var(--gg-space-4)] py-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] font-bold text-primary-text hover:bg-surface-sunken"
      >
        查看更多商品 →
      </Link>
    </div>
  );
}
