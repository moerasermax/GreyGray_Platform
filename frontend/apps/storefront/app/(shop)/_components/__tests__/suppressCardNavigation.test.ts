/**
 * 收藏心不可以把人帶去商品頁——「現在卡在哪」#27 的迴歸測試。
 *
 * ── 這個 bug 長什麼樣 ──
 * 商品列表把整張卡包在 `<Link>` 裡（同層的 `../ProductCardLink.tsx`），
 * 而收藏心外面那層只寫了 `onClick={(e) => e.stopPropagation()}`。
 * `stopPropagation()` 擋得住 `next/link` 掛在 `<a>` 上的 `onClick`，
 * **擋不住 `<a>` 本身的預設導航**——預設行為是在事件傳播結束「之後」才執行的。
 * 結果：在真 Chrome 上點愛心，收藏不會切換，整頁跳去 `/products/{id}`。
 *
 * ── 為什麼測試放在 storefront，而不是元件旁邊 ──
 * 受測的 `suppressCardNavigation` 與 `ProductCard` 都住在 `packages/ui`，
 * 但**那個套件沒有 `test` script 也沒有 `vitest` 相依**，
 * 放在它底下的測試不會被 `pnpm --recursive test` 跑到（FE-22 第一版就是這樣，
 * 寫對了卻永遠是休眠的）。而補相依必須動 `packages/ui/package.json`
 * 與 `pnpm-lock.yaml`，兩者都被派工書擋住。
 *
 * 改放在**消費端**就完全避開這個問題，零相依變更：
 *   · `apps/storefront` 依賴 `@greygray/ui`（`workspace:*`）而且有 vitest
 *   · `suppressCardNavigation` 已經從 `packages/ui/src/index.ts` 公開 export
 *   · 這裡也正是 bug 真正發作的地方——`ProductCardLink` 就在隔壁
 *
 * ── 為什麼是這種寫法的測試 ──
 * 這個 workspace 沒有 jsdom、沒有 `@testing-library`，也不打算為此加相依，
 * 所以寫不出「模擬點擊愛心、斷言網址沒變」。改成守住這個 bug 的**形狀**，
 * 分兩段各自可驗：
 *   1. `suppressCardNavigation` 餵假 event，斷言 `preventDefault` 與
 *      `stopPropagation` **兩個都**被呼叫。少了前者就是 #27 本身。
 *   2. `ProductCard` 真的把它接到收藏心外層——沒有這一段的話，
 *      有人把 `onClick` 改回舊寫法、①仍然全綠，這個檔案就變成擺設。
 *
 * ②不必渲染：`ProductCard` 是純函式元件，直接呼叫就會拿到 React element 樹，
 * 用結構走訪即可。JSX 走 classic transform（`tsconfig.base.json` 是 `jsx: preserve`，
 * vitest 的 esbuild 因此產 `React.createElement`），而 `ProductCard.tsx` 自己的
 * 模組作用域裡沒有 `React`，所以要把 `React` 掛到 global 上——與
 * `admin .../__tests__/dashboardLedger.test.tsx` 同一個理由、同一個做法。
 */
import * as React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { FavoriteHeart, ProductCard, suppressCardNavigation } from '@greygray/ui';

(globalThis as unknown as { React: typeof React }).React = React;

/** 假的 event：只有 `suppressCardNavigation` 用得到的兩個方法。 */
function fakeEvent() {
  return { preventDefault: vi.fn(), stopPropagation: vi.fn() };
}

/** element 的直屬 children，一律攤平成陣列，單一子節點與 `undefined` 都吃得下。 */
function childrenOf(element: React.ReactElement): React.ReactNode[] {
  const { children } = element.props as { children?: React.ReactNode };
  return React.Children.toArray(children);
}

/** 深度優先走訪 React element 樹，回傳第一個符合條件的 element。 */
function findElement(
  node: React.ReactNode,
  predicate: (element: React.ReactElement) => boolean,
): React.ReactElement | undefined {
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElement(child, predicate);
      if (found) return found;
    }
    return undefined;
  }

  if (!React.isValidElement(node)) return undefined;
  if (predicate(node)) return node;

  return findElement(childrenOf(node), predicate);
}

describe('suppressCardNavigation', () => {
  it('呼叫 preventDefault——少了它，<a> 的預設導航就擋不住（#27 本身）', () => {
    const event = fakeEvent();

    suppressCardNavigation(event);

    expect(event.preventDefault).toHaveBeenCalledTimes(1);
  });

  it('呼叫 stopPropagation——少了它，next/link 的 onClick 仍會做 client-side 導航', () => {
    const event = fakeEvent();

    suppressCardNavigation(event);

    expect(event.stopPropagation).toHaveBeenCalledTimes(1);
  });
});

describe('ProductCard 的收藏心', () => {
  const props = {
    imageAlt: '測試商品',
    name: '測試商品',
    price: { amountMinor: 18000, currency: 'TWD' } as const,
  };

  /** 直接包住 `FavoriteHeart` 的那一層；`onToggleFavorite` 有給時才存在。 */
  function favoriteWrapper(onToggleFavorite: (() => void) | undefined) {
    const tree = ProductCard({ ...props, onToggleFavorite });
    return findElement(tree, (element) =>
      childrenOf(element).some(
        (child) => React.isValidElement(child) && child.type === FavoriteHeart,
      ),
    );
  }

  it('外層真的接上 suppressCardNavigation，而不是只把函式留在檔案裡', () => {
    const wrapper = favoriteWrapper(() => {});

    expect(wrapper).toBeDefined();
    expect((wrapper?.props as { onClick?: unknown }).onClick).toBe(suppressCardNavigation);
  });

  it('沒有 onToggleFavorite 時不畫收藏心，也就沒有要攔的導航', () => {
    expect(favoriteWrapper(undefined)).toBeUndefined();
  });
});
