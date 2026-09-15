import { listCampaigns, listCategories, listProducts } from '@greygray/api-client/endpoints/storefront';
import { serverApi } from '../_lib/apiClient';
import { HomeSearchHeader } from './_components/HomeSearchHeader';
import { HeroBanner } from './_components/HeroBanner';
import { CategoryRail } from './_components/CategoryRail';
import { ProductWall } from './_components/ProductWall';
import { InfoLinks } from '../(info)/_components/InfoLinks';

/**
 * 首頁走 SSR：搜尋列 ＋ 頭像 → 柔粉漸層 banner ＋ 圓形產品 →
 * 橫捲圓形分類標 → 商品卡牆（ADR-009）。
 */
export default async function HomePage() {
  const api = await serverApi();
  const [categories, products, openCampaigns] = await Promise.all([
    listCategories(api),
    listProducts(api, { limit: 12 }),
    listCampaigns(api, { status: 'Open', limit: 1 }),
  ]);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-4)]">
      <HomeSearchHeader />

      <HeroBanner campaign={openCampaigns.items[0]} />

      <section className="flex flex-col gap-[var(--gg-space-3)]">
        <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">逛分類</h2>
        <CategoryRail categories={categories} />
      </section>

      <section className="flex flex-col gap-[var(--gg-space-3)]">
        <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">為你精選</h2>
        <ProductWall products={products.items} />
      </section>

      {/* 匿名訪客的唯一入口——「我的」要登入才看得到，見 (info)/_components/InfoLinks.tsx */}
      <footer className="border-t border-border-soft pt-[var(--gg-space-4)]">
        <InfoLinks />
      </footer>
    </main>
  );
}
