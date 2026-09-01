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
import { useEffect, useSyncExternalStore } from 'react';
import { getCart } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from '../_lib/apiClient';
import { cartTabAccessibleName, formatCartBadge } from '../_lib/cartBadge';
import {
  getCartItemCount,
  getServerCartItemCount,
  publishCart,
  subscribeCartItemCount,
} from '../_lib/cartCountStore';
import {
  STOREFRONT_TABS,
  TAB_BAR_HEIGHT,
  TAB_BAR_SAFE_AREA_PADDING,
  activeTabHref,
  shouldShowTabBar,
} from '../_lib/tabs';
import { TabBarIcon } from './TabBarIcons';

export function StorefrontTabBar() {
  const pathname = usePathname();
  const itemCount = useSyncExternalStore(
    subscribeCartItemCount,
    getCartItemCount,
    getServerCartItemCount,
  );

  const visible = shouldShowTabBar(pathname);

  useEffect(() => {
    // 已經知道件數就不再問——三個呼叫點會把最新的 Cart 推進 store。
    if (!visible || getCartItemCount() !== null) return;

    let cancelled = false;
    getCart(browserApi())
      .then((cart) => {
        if (!cancelled) publishCart(cart);
      })
      .catch(() => {
        // 拿不到就維持「不知道」＝不畫徽章。**絕對不要退化成 0。**
      });

    return () => {
      cancelled = true;
    };
  }, [visible, pathname]);

  if (!visible) return null;

  const active = activeTabHref(pathname);
  const badge = formatCartBadge(itemCount);

  return (
    <nav
      aria-label="主要導覽"
      className={
        'fixed inset-x-0 bottom-0 z-[var(--gg-z-bottom-bar)] flex items-stretch ' +
        'border-t border-border-soft bg-surface shadow-bottom-bar'
      }
      style={{ minHeight: TAB_BAR_HEIGHT, paddingBottom: TAB_BAR_SAFE_AREA_PADDING }}
    >
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
              (isActive ? 'text-primary-text' : 'text-fg-muted')
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
    </nav>
  );
}
