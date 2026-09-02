'use client';

/**
 * 讀購物車件數給徽章用。**分頁列與頂部列共用這一支**，不是各寫一份。
 *
 * 為什麼要共用：兩條殼上的購物車徽章必須永遠一致——同一份 store、同一個
 * 「還不知道就不問第二次」的規則、同一條「拿不到就維持不知道、絕不退化成 0」。
 * FE-23 把這段邏輯寫在 `StorefrontTabBar` 裡，FE-24 加頂部列時原樣複製一份
 * 就是在製造兩份會各自漂移的規則。
 *
 * `enabled`：這一頁根本不畫徽章時（例如購物車頁的頂部列右邊是空的）
 * 不要多打一支 API。**注意這支 API 對匿名訪客會在後端生出一張購物車**
 * （FE-23 交付時已提報的行為改變），所以「不需要就不要問」不只是省流量。
 */
import { useEffect, useSyncExternalStore } from 'react';
import { getCart } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from './apiClient';
import type { CartItemCount } from './cartBadge';
import {
  getCartItemCount,
  getServerCartItemCount,
  publishCart,
  subscribeCartItemCount,
} from './cartCountStore';

/**
 * @param enabled 這一頁要不要畫徽章。`false` 就完全不打 API。
 * @param retryOn 值變了而且**仍然不知道**件數時再問一次。傳 `usePathname()` 進來，
 *   上一次失敗過的人換一頁就會重試，不必等到重新整理。
 */
export function useCartItemCount(enabled: boolean, retryOn?: string): CartItemCount {
  const itemCount = useSyncExternalStore(
    subscribeCartItemCount,
    getCartItemCount,
    getServerCartItemCount,
  );

  useEffect(() => {
    // 已經知道件數就不再問——寫入端點會把最新的 Cart 推進 store。
    if (!enabled || getCartItemCount() !== null) return;

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
  }, [enabled, retryOn]);

  return itemCount;
}
