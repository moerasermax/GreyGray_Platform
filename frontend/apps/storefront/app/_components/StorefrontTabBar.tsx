'use client';

/*
 * 前台的殼：底部分頁列（首頁 · 開團 · 購物車 · 我的）。
 * 修「現在卡在哪」#30——加完購物車之後沒有任何按鈕回得去，首頁上連「購物車」三個字都沒有。
 *
 * ── 高度為什麼寫成 `TAB_BAR_HEIGHT` 而不是自己挑一個 ──
 * `globals.css` 早就給 `body` 補了
 * `padding-bottom: calc(var(--gg-bottom-bar-height) + env(safe-area-inset-bottom, 0px))`，
 * 那是全站唯一的底部留白，**每一頁都有**。分頁列的高度寫成一模一樣的算式，
 * 內容就恰好不會被蓋住，也不會多出一整條的空白。
 * 兩邊必須逐字相同，`_lib/__tests__/tabBarReservesBottomSpace.test.ts` 逐字比對釘住。
 *
 * ── 徽章為什麼可以「不重新整理就更新」 ──
 * 三支購物車寫入端點都回傳更新後的整個 `Cart`，呼叫端直接 `publishCart(回傳值)`，
 * 這裡靠 `useSyncExternalStore` 讀同一份模組 store。數字始終來自伺服器，
 * 不是前端自己 +1 猜的。細節見 `_lib/cartCountStore.ts`。
 */

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useEffect, useState } from 'react';
import { cartTabAccessibleName, formatCartBadge } from '../_lib/cartBadge';
import {
  STOREFRONT_TABS,
  TAB_BAR_HEIGHT,
  TAB_BAR_SAFE_AREA_PADDING,
  activeTabHref,
  shouldShowTabBar,
} from '../_lib/tabs';
import { useCartItemCount } from '../_lib/useCartItemCount';
import { TabBarIcon } from './TabBarIcons';

export function StorefrontTabBar() {
  const pathname = usePathname();
  /*
   * ── 查詢字串為什麼不是用 `useSearchParams()` ──
   * `/login?next=%2Fcheckout` 要亮「購物車」而不是「我的」（FE-25 ⑦），所以這裡需要
   * 查詢字串——但 `usePathname()` 不含它，而 `useSearchParams()` 會把用它的元件
   * **推進 CSR bailout**。這支元件掛在**根 layout** 上，等於全站的靜態產生一起陪葬。
   *
   * 改成 `authRedirect.ts` 那一招：只在瀏覽器端讀 `window.location.search`。
   * SSR 與首次渲染看不到查詢字串（`''`），亮的是 `/login` 本來的分頁「我的」，
   * 掛載後這個 effect 補上真正的查詢字串再算一次，才變成「購物車」。
   * 那一瞬間的差異是**同一條分頁列上換一個圖示變色**，不是內容有無，
   * 也不會造成 hydration 不一致（第一次渲染兩邊都是 `''`）。
   *
   * 依 `pathname` 重跑：Next.js 的軟導向不會重新掛載這支元件，
   * 少了這條相依，從 `/login?next=…` 走到別頁時會停在舊的查詢字串上。
   */
  const [search, setSearch] = useState('');
  useEffect(() => {
    setSearch(typeof window === 'undefined' ? '' : window.location.search);
  }, [pathname]);

  const visible = shouldShowTabBar(pathname);
  // 取數的規則（何時問、拿不到怎麼辦）與頂部列共用同一支 hook，見 `_lib/useCartItemCount.ts`。
  const itemCount = useCartItemCount(visible, pathname);

  if (!visible) return null;

  const active = activeTabHref(pathname, search);
  const badge = formatCartBadge(itemCount);

  return (
    <nav
      aria-label="主要導覽"
      className={
        'fixed inset-x-0 bottom-0 z-[var(--gg-z-bottom-bar)] flex items-stretch justify-center ' +
        'border-t border-border-soft bg-surface shadow-bottom-bar'
      }
      style={{ minHeight: TAB_BAR_HEIGHT, paddingBottom: TAB_BAR_SAFE_AREA_PADDING }}
    >
      {/*
       * 底色與高度留在外層 <nav>（滿版、算式不動）；項目放進這個有限寬度的容器。
       * 手機：容器滿寬、四等分。桌面（md 以上）：集中在中間、圖文橫排，
       * 不再是全螢幕寬度上四個孤立的圖示。
       */}
      <div className="flex w-full items-stretch md:max-w-xl md:gap-[var(--gg-space-2)] md:px-[var(--gg-space-4)]">
      {STOREFRONT_TABS.map((tab) => {
        const isActive = tab.href === active;
        const isCart = tab.icon === 'cart';

        return (
          <Link
            key={tab.href}
            href={tab.href}
            aria-current={isActive ? 'page' : undefined}
            aria-label={isCart ? cartTabAccessibleName(itemCount) : undefined}
            className={
              'flex flex-1 flex-col items-center justify-center gap-[var(--gg-space-1)] ' +
              'py-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] font-bold no-underline ' +
              'transition-colors duration-[var(--gg-duration-base)] ease-out-soft ' +
              'rounded-card focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary ' +
              'md:my-[var(--gg-space-2)] md:flex-row md:gap-[var(--gg-space-3)] md:text-[length:var(--gg-text-sm)] ' +
              (isActive ? 'text-primary-text md:bg-surface-sunken' : 'text-fg-muted hover:text-fg')
            }
          >
            <span className="relative flex text-[length:var(--gg-text-xl)] leading-none">
              <TabBarIcon name={tab.icon} />
              {isCart && badge !== null && (
                /*
                 * 徽章只在 `badge !== null` 時存在——「不知道幾件」與「空車」都不畫。
                 * `aria-hidden`：件數已經唸在整個分頁的 aria-label 裡了，
                 * 讓螢幕閱讀器再唸一次數字只是重複。
                 */
                <span
                  aria-hidden={true}
                  className={
                    'absolute -right-[var(--gg-space-3)] -top-[var(--gg-space-2)] ' +
                    'inline-flex min-w-[var(--gg-space-4)] items-center justify-center ' +
                    'rounded-pill bg-primary px-[var(--gg-space-1)] ' +
                    'text-[length:var(--gg-text-xs)] font-bold leading-tight text-on-primary'
                  }
                >
                  {badge}
                </span>
              )}
            </span>
            {tab.label}
          </Link>
        );
      })}
      </div>
    </nav>
  );
}
