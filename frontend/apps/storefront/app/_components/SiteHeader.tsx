'use client';

/*
 * 全站桌面頁首（FE-44）：品牌 · 搜尋 · 全部商品／開團／購買流程／常見問題 · 購物車 · 會員。
 *
 * ── 為什麼只在 `lg` 以上出現 ──
 * 桌面原本只有首頁有品牌頁首，其他頁只剩螢幕底部的手機式分頁列，整站看起來像放大的
 * 手機 App。手機的殼（底部分頁列、`PageTopBar`）已經過 #30／#32 兩次驗收，
 * 這一包刻意**完全不動手機**：桌面（`lg` 以上）換成這條頁首，`lg` 以下由 CSS 隱藏，
 * 分頁列與 `PageTopBar` 則反過來在 `lg` 以上隱藏。
 *
 * ── 哪些頁不顯示 ──
 * 只有 `/payment/:orderId`。理由與 `_lib/tabs.ts` 的 `TAB_BAR_RULES` 同一條：
 * 那一頁載入後自動 POST 導轉綠界，放任何出口都是邀請使用者把付款丟在半路。
 * 判斷直接問 `tabBarRuleFor(pathname)`，不另寫一套路由比對——兩份規則漂移的代價比重用高。
 *
 * ── 選中態怎麼算 ──
 * 分頁列的 `activeTabHref` 已經處理了帳號頁家族（`/orders`、`/favorites`…亮「我的」）
 * 與 `/login?next=%2Fcheckout` 亮購物車那一條（FE-25 ⑦），這裡沿用它，
 * 只補分頁列沒有的分類：`/products/*`、`/categories/*` 亮「全部商品」、
 * `/guide`／`/faq` 亮自己。純函式 `siteHeaderActiveHref` 抽出來是為了可以不靠 jsdom 測。
 */

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { Avatar, IconCart, SearchBar } from '@greygray/ui';
import { cartTabAccessibleName, formatCartBadge } from '../_lib/cartBadge';
import { activeTabHref, normalizePathname, tabBarRuleFor } from '../_lib/tabs';
import { useCartItemCount } from '../_lib/useCartItemCount';

const FOCUS_RING =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary';

/** 頁首的瀏覽入口。與首頁 `HomeSearchHeader` 同一份清單：都是站上既有的頁面，不在這裡發明新路由。 */
const NAV_LINKS = [
  { href: '/products', label: '全部商品' },
  { href: '/campaigns', label: '開團' },
  { href: '/guide', label: '購買流程' },
  { href: '/faq', label: '常見問題' },
] as const;

/** 全部商品：商品列表、商品詳情、分類頁都算。 */
const PRODUCTS_PREFIXES = ['/products', '/categories'] as const;

function hasPrefix(path: string, prefix: string): boolean {
  return path === prefix || path.startsWith(`${prefix}/`);
}

/**
 * 這一頁在頁首該亮哪個入口。回傳值是 `NAV_LINKS` 的 href、`'/cart'`、`'/me'` 之一，
 * 或 `null`（首頁與不屬於任何入口的頁面，例如 `/terms`）。
 *
 * - `/products`、`/products/*`、`/categories/*` → 全部商品
 * - `/campaigns`、`/campaigns/*` → 開團
 * - `/guide`、`/faq` → 自己
 * - 其餘交給分頁列的 `activeTabHref`（`/cart`、`/checkout` → 購物車；帳號頁家族 → 我的；
 *   `/login?next=%2Fcheckout` → 購物車）。它回 `'/'` 時代表首頁，頁首沒有「首頁」入口，回 `null`。
 */
export function siteHeaderActiveHref(pathname: string, search?: string | null): string | null {
  const path = normalizePathname(pathname);

  if (PRODUCTS_PREFIXES.some((prefix) => hasPrefix(path, prefix))) return '/products';
  if (hasPrefix(path, '/campaigns')) return '/campaigns';
  if (path === '/guide' || path === '/faq') return path;

  const tab = activeTabHref(pathname, search);
  return tab === null || tab === '/' ? null : tab;
}

/** 這一頁要不要畫頁首。只有 `TAB_BAR_RULES` 裡「付款導轉頁」那一條會關掉它。 */
export function shouldShowSiteHeader(pathname: string): boolean {
  return tabBarRuleFor(pathname)?.route !== '/payment/:orderId';
}

const ICON_LINK =
  'relative inline-flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] shrink-0 items-center ' +
  'justify-center rounded-pill text-fg no-underline transition-colors ' +
  `duration-[var(--gg-duration-fast)] hover:bg-surface-sunken ${FOCUS_RING}`;

export function SiteHeader() {
  const pathname = usePathname();
  const router = useRouter();
  const [value, setValue] = useState('');

  /*
   * 查詢字串的取法與 `StorefrontTabBar` 相同：不用 `useSearchParams()`（會把根 layout
   * 推進 CSR bailout），掛載後才從 `window.location.search` 補上。首次渲染兩邊都是 `''`，
   * 不會造成 hydration 不一致；差異只是 `/login?next=…` 那一瞬間亮的入口。
   */
  const [search, setSearch] = useState('');
  useEffect(() => {
    setSearch(typeof window === 'undefined' ? '' : window.location.search);
  }, [pathname]);

  const visible = shouldShowSiteHeader(pathname);
  // 取數規則與分頁列、頂部列共用同一支 hook（`_lib/useCartItemCount.ts`），不自己算數字。
  const itemCount = useCartItemCount(visible, pathname);

  if (!visible) return null;

  const active = siteHeaderActiveHref(pathname, search);
  const badge = formatCartBadge(itemCount);

  /** 送出搜尋就導去商品列表頁——與首頁 `HomeSearchHeader` 同一個行為，不改搜尋邏輯。 */
  function handleSubmit(keyword: string) {
    const trimmed = keyword.trim();
    router.push(trimmed ? `/products?q=${encodeURIComponent(trimmed)}` : '/products');
  }

  return (
    /*
     * `hidden lg:block`：手機完全不畫。`sticky` 而不是 `fixed`，理由與 `TopBar` 相同——
     * 仍佔位置，內容自然往下排，不需要任何一處補頂部留白。
     * 白底、底部細邊框，不加陰影堆疊或漸層（FE-38 的克制方向）。
     */
    <header className="sticky top-0 z-[var(--gg-z-header)] hidden border-b border-border-soft bg-surface lg:block">
      <div
        className="mx-auto flex max-w-[var(--gg-container-max)] items-center gap-[var(--gg-space-5)] px-[var(--gg-space-4)]"
        style={{ minHeight: 'var(--gg-top-bar-height)' }}
      >
        {/* 品牌標記沿用首頁的寫法；這裡刻意不是 <h1>，每一頁都已經有自己的 <h1>。 */}
        <Link
          href="/"
          className={`flex min-h-[var(--gg-touch-min)] shrink-0 items-center gap-[var(--gg-space-2)] rounded-[var(--gg-radius-sm)] no-underline ${FOCUS_RING}`}
        >
          <span aria-hidden="true" className="h-[var(--gg-space-3)] w-[var(--gg-space-3)] rounded-pill bg-primary" />
          <span className="font-display text-[length:var(--gg-text-xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
            GreyGray
          </span>
          <span className="border-l border-border-strong pl-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] font-bold tracking-[var(--gg-tracking-eyebrow)] text-fg-muted">
            選品代購
          </span>
        </Link>

        <SearchBar
          value={value}
          onChange={setValue}
          onSubmit={handleSubmit}
          className="min-w-0 flex-1"
        />

        <nav aria-label="主要導覽" className="shrink-0">
          <ul className="flex items-center gap-[var(--gg-space-1)]">
            {NAV_LINKS.map((link) => {
              const isActive = link.href === active;
              return (
                <li key={link.href}>
                  <Link
                    href={link.href}
                    aria-current={isActive ? 'page' : undefined}
                    className={
                      'flex min-h-[var(--gg-touch-min)] items-center rounded-pill px-[var(--gg-space-3)] ' +
                      'text-[length:var(--gg-text-sm)] font-bold no-underline transition-colors ' +
                      `duration-[var(--gg-duration-fast)] ${FOCUS_RING} ` +
                      (isActive
                        ? 'bg-surface-sunken text-primary-text'
                        : 'text-fg-muted hover:bg-surface-sunken hover:text-fg')
                    }
                  >
                    {link.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>

        <Link
          href="/cart"
          aria-label={cartTabAccessibleName(itemCount)}
          aria-current={active === '/cart' ? 'page' : undefined}
          className={`${ICON_LINK} text-[length:var(--gg-text-lg)] ${active === '/cart' ? 'bg-surface-sunken text-primary-text' : ''}`}
        >
          <IconCart />
          {badge !== null && (
            /*
             * 徽章只在 `badge !== null` 時存在——「不知道幾件」與「空車」都不畫。
             * `aria-hidden`：件數已經唸在連結的 aria-label 裡，再唸一次只是重複。
             * 規則與分頁列、頂部列**同一套函式**（`_lib/cartBadge.ts`）。
             */
            <span
              aria-hidden={true}
              className={
                'absolute right-0 top-0 inline-flex min-w-[var(--gg-space-4)] items-center ' +
                'justify-center rounded-pill bg-primary px-[var(--gg-space-1)] ' +
                'text-[length:var(--gg-text-xs)] font-bold leading-tight text-on-primary'
              }
            >
              {badge}
            </span>
          )}
        </Link>

        {/* 會員頁是 `(account)/me`——訂單、地址、儲值金與登出都從那裡進去。 */}
        <Link
          href="/me"
          aria-label="會員中心"
          aria-current={active === '/me' ? 'page' : undefined}
          className={`${ICON_LINK} ${active === '/me' ? 'bg-surface-sunken' : ''}`}
        >
          <Avatar alt="會員中心" />
        </Link>
      </div>
    </header>
  );
}
