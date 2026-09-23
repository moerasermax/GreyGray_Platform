'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Avatar, SearchBar } from '@greygray/ui';

const FOCUS_RING =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary';

/** 首頁標頭的瀏覽入口。都是站上既有的頁面，不在這裡發明新路由。 */
const NAV_LINKS = [
  { href: '/products', label: '全部商品' },
  { href: '/campaigns', label: '開團' },
  { href: '/guide', label: '購買流程' },
  { href: '/faq', label: '常見問題' },
] as const;

/**
 * 首頁標頭：品牌 ＋ 搜尋 ＋ 瀏覽入口 ＋ 會員（ADR-009 首頁切版第一段；FE-38 改版）。
 * 送出搜尋就導去商品列表頁——這個行為沒有變。
 *
 * 手機：品牌與會員一列、搜尋一列、入口一列。桌面（lg 以上）整個隱藏，
 * 由全站 `SiteHeader`（FE-44）負責，免得首頁出現雙頁首；原本的 lg 排版樣式保留不動。
 * 用 `order` 排而不是渲染兩份，免得同一個連結在 DOM 裡出現兩次、Tab 要走兩遍。
 */
export function HomeSearchHeader() {
  const router = useRouter();
  const [value, setValue] = useState('');

  function handleSubmit(keyword: string) {
    const trimmed = keyword.trim();
    router.push(trimmed ? `/products?q=${encodeURIComponent(trimmed)}` : '/products');
  }

  return (
    <header className="flex flex-wrap items-center gap-x-[var(--gg-space-5)] gap-y-[var(--gg-space-3)] border-b border-border-soft pb-[var(--gg-space-4)] lg:hidden">
      {/* 首頁的 <h1> 就是品牌名；主視覺與各區塊從 <h2> 開始。 */}
      <h1 className="order-1 shrink-0">
        <Link
          href="/"
          className={`flex min-h-[var(--gg-touch-min)] items-center gap-[var(--gg-space-2)] rounded-[var(--gg-radius-sm)] ${FOCUS_RING}`}
        >
          <span aria-hidden="true" className="h-[var(--gg-space-3)] w-[var(--gg-space-3)] rounded-pill bg-primary" />
          <span className="font-display text-[length:var(--gg-text-xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
            GreyGray
          </span>
          <span className="border-l border-border-strong pl-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] font-bold tracking-[var(--gg-tracking-eyebrow)] text-fg-muted">
            選品代購
          </span>
        </Link>
      </h1>

      <SearchBar
        value={value}
        onChange={setValue}
        onSubmit={handleSubmit}
        className="order-3 min-w-0 basis-full lg:order-2 lg:flex-1 lg:basis-0"
      />

      <nav aria-label="商店瀏覽" className="order-4 -mx-[var(--gg-space-2)] min-w-0 basis-full overflow-x-auto lg:order-3 lg:mx-0 lg:basis-auto">
        {/* 留 4px 內距：外層會橫向捲動（overflow 會裁切），不留的話 focus 框會被切掉。 */}
        <ul className="flex items-center gap-[var(--gg-space-1)] p-[var(--gg-space-1)]">
          {NAV_LINKS.map((link) => (
            <li key={link.href} className="shrink-0">
              <Link
                href={link.href}
                className={`flex min-h-[var(--gg-touch-min)] items-center rounded-pill px-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] font-bold text-fg-muted transition-colors duration-[var(--gg-duration-fast)] hover:bg-surface-sunken hover:text-fg ${FOCUS_RING}`}
              >
                {link.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      {/* 會員頁是 `(account)/me`——訂單、地址、儲值金與登出都從那裡進去。 */}
      <Link
        href="/me"
        aria-label="會員中心"
        className={`order-2 ml-auto flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] shrink-0 items-center justify-center rounded-pill lg:order-4 lg:ml-0 ${FOCUS_RING}`}
      >
        <Avatar alt="會員中心" />
      </Link>
    </header>
  );
}
