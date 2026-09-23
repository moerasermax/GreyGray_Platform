'use client';

/*
 * 三頁（商品詳情 · 購物車 · 結帳）的頂部列。修「現在卡在哪」#32——
 * 這三頁畫面上一個出口都沒有，而它們正好是分頁列刻意隱藏的三頁。
 *
 * 分工：長相在 `@greygray/ui` 的 `TopBar`（token、sticky、safe-area，不碰 router），
 * 行為在這裡（返回去哪、購物車徽章）。判斷本身是 `_lib/topBar.ts` 的純函式，
 * 因為這個 workspace 沒有 jsdom，渲染層測不到。
 */

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { IconCart, IconChevronLeft, TopBar } from '@greygray/ui';
import { cartTabAccessibleName, formatCartBadge } from '../_lib/cartBadge';
import { backHrefFor, shouldShowCartLink, shouldUseHistoryBack } from '../_lib/topBar';
import { useCartItemCount } from '../_lib/useCartItemCount';

export interface PageTopBarProps {
  /** 中間那行字。商品頁傳商品名（只有那一頁知道），購物車與結帳傳固定的頁名。 */
  title: string;
}

export function PageTopBar({ title }: PageTopBarProps) {
  const pathname = usePathname();
  const backHref = backHrefFor(pathname);
  const withCart = shouldShowCartLink(pathname);
  const itemCount = useCartItemCount(withCart, pathname);
  const badge = formatCartBadge(itemCount);

  /*
   * 返回是真的 `<a href>`（`next/link`），指向 `_lib/topBar.ts` 的後備目標：
   * 沒有 JS 也走得動、鍵盤到得了、右鍵可以「在新分頁開啟」。
   * 有站內上一頁時才攔下來走 `history.back()`（回去比較符合預期，捲動位置也還在）；
   * 判斷不成立就讓瀏覽器照 href 走。
   * **任何一種判斷失準的後果都只是「回到後備目標」，不會是「按了沒反應」。**
   */
  function handleBack(event: React.MouseEvent<HTMLAnchorElement>) {
    // 有修飾鍵或中鍵（在新分頁開啟）時完全不要插手。
    if (event.defaultPrevented) return;
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) {
      return;
    }
    if (typeof window === 'undefined') return;
    const canGoBack = shouldUseHistoryBack({
      referrer: document.referrer,
      origin: window.location.origin,
      historyLength: window.history.length,
    });
    if (!canGoBack) return;

    event.preventDefault();
    window.history.back();
  }

  // 沒有規則的路由不畫頂部列。正常情況下這三頁以外沒有人會 render 它，
  // 但 `backHref` 是 `string | null`，不畫比畫一顆去不了任何地方的返回鍵誠實。
  if (backHref === null) return null;

  return (
    <TopBar
      title={title}
      // lg 以上由全站 SiteHeader（FE-44）提供出口；走 TopBar 自己的 className，
      // 不外包一層 div——那會讓 sticky 在手機失效。
      className="lg:hidden"
      left={
        <Link
          href={backHref}
          onClick={handleBack}
          aria-label="返回"
          className={
            'inline-flex aspect-square items-center justify-center rounded-pill ' +
            'p-[var(--gg-space-2)] text-[length:var(--gg-text-lg)] text-fg no-underline ' +
            'transition-colors duration-[var(--gg-duration-base)] ease-out-soft hover:bg-surface-sunken'
          }
        >
          <IconChevronLeft />
        </Link>
      }
      right={
        withCart ? (
          <Link
            href="/cart"
            aria-label={cartTabAccessibleName(itemCount)}
            className={
              'relative inline-flex aspect-square items-center justify-center rounded-pill ' +
              'p-[var(--gg-space-2)] text-[length:var(--gg-text-lg)] text-fg no-underline ' +
              'transition-colors duration-[var(--gg-duration-base)] ease-out-soft hover:bg-surface-sunken'
            }
          >
            <IconCart />
            {badge !== null && (
              /*
               * 徽章只在 `badge !== null` 時存在——「不知道幾件」與「空車」都不畫。
               * `aria-hidden`：件數已經唸在整個連結的 aria-label 裡，再唸一次只是重複。
               * 規則與分頁列**同一套函式**（`_lib/cartBadge.ts`），不是複製過來的。
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
        ) : undefined
      }
    />
  );
}
