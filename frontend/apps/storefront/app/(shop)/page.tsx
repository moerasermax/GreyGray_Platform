import type { ReactNode } from 'react';
import Link from 'next/link';
import { listCampaigns, listCategories, listProducts } from '@greygray/api-client/endpoints/storefront';
import { serverApi } from '../_lib/apiClient';
import { HomeSearchHeader } from './_components/HomeSearchHeader';
import { HeroBanner } from './_components/HeroBanner';
import { CategoryRail } from './_components/CategoryRail';
import { ProductWall } from './_components/ProductWall';
import { InfoLinks } from '../(info)/_components/InfoLinks';
import { onlyRootCategories } from '../_lib/categoryTree';

/**
 * 首頁走 SSR：品牌標頭（搜尋 ＋ 入口 ＋ 會員）→ 主視覺（開團，或沒有開團時的品牌介紹）→
 * 橫捲圓形分類標 → 商品卡牆（ADR-009；FE-38 調整留白與段落層級）。
 */
export default async function HomePage() {
  const api = await serverApi();
  const [categories, products, openCampaigns] = await Promise.all([
    listCategories(api),
    listProducts(api, { limit: 12 }),
    listCampaigns(api, { status: 'Open', limit: 1 }),
  ]);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-4)] lg:gap-[var(--gg-space-7)] lg:px-[var(--gg-space-5)] lg:py-[var(--gg-space-5)]">
      <HomeSearchHeader />

      {/*
       * FE-47：`HomeSearchHeader`（首頁唯一可見的 <h1>）在 lg 以上整個 `lg:hidden`，
       * 桌面首頁的 <h1> 數會變成 0。這裡補一個只給輔助科技的 <h1>：
       * `hidden lg:block` 讓它在 lg 以下根本不進 DOM 的可讀樹（display:none），
       * `sr-only` 讓它在 lg 以上不佔版面——任何寬度下可讀的 <h1> 恰好一個。
       * 文字與 `HomeSearchHeader` 的品牌標題一致；不改 SiteHeader、HomeSearchHeader 與主視覺。
       */}
      <h1 className="sr-only hidden lg:block">GreyGray 選品代購</h1>

      <HeroBanner campaign={openCampaigns.items[0]} />

      <HomeSection id="home-categories" title="逛分類" description="依類別找商品。">
        <CategoryRail categories={onlyRootCategories(categories)} />
      </HomeSection>

      <HomeSection
        id="home-products"
        title="店內商品"
        description="先看看這幾件，完整清單在商品列表。"
        allHref="/products"
        allLabel="查看全部商品"
      >
        <ProductWall products={products.items} />
      </HomeSection>

      {/* 匿名訪客的唯一入口——「我的」要登入才看得到，見 (info)/_components/InfoLinks.tsx */}
      <footer className="border-t border-border-soft pt-[var(--gg-space-5)]">
        <InfoLinks />
      </footer>
    </main>
  );
}

/** 首頁區塊的共同骨架：標題 ＋ 一句說明 ＋（有的話）「查看全部」。只在這一頁用，所以不抽到共用元件。 */
function HomeSection({
  id,
  title,
  description,
  allHref,
  allLabel,
  children,
}: {
  id: string;
  title: string;
  description: string;
  allHref?: string;
  allLabel?: string;
  children: ReactNode;
}) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-[var(--gg-space-4)]">
      <div className="flex items-end justify-between gap-[var(--gg-space-4)]">
        <div className="flex min-w-0 flex-col gap-[var(--gg-space-1)]">
          <h2
            id={id}
            className="font-display text-[length:var(--gg-text-xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg"
          >
            {title}
          </h2>
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{description}</p>
        </div>
        {allHref ? (
          <Link
            href={allHref}
            aria-label={allLabel}
            className="flex min-h-[var(--gg-touch-min)] shrink-0 items-center rounded-pill px-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] font-bold text-primary-text transition-colors duration-[var(--gg-duration-fast)] hover:bg-primary-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-strong"
          >
            查看全部
            <span aria-hidden="true" className="pl-[var(--gg-space-1)]">
              →
            </span>
          </Link>
        ) : null}
      </div>
      {children}
    </section>
  );
}
