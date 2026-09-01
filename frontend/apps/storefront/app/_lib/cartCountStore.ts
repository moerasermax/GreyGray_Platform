/**
 * 購物車件數的跨頁共享狀態。
 *
 * ── 為什麼是模組層級的 store 而不是 React Context ──
 * 徽章畫在**根 layout** 的分頁列上，而更新它的人散在三個不同 route group 的頁面裡
 * （商品詳情、開團詳情、購物車頁）。用 Context 的話得在 `layout.tsx` 包一層
 * client provider 把整棵樹關進 client boundary；用模組 store ＋ `useSyncExternalStore`
 * 則只有分頁列自己是 client 元件，其餘頁面維持原樣。
 *
 * ── 為什麼「加入購物車後徽章立刻更新」不需要重打一次 API ──
 * `addCartLine`／`updateCartLine`／`removeCartLine` **三支都回傳更新後的整個 `Cart`**
 * （見 `packages/api-client/src/endpoints/storefront.ts`）。
 * 所以呼叫端直接把回傳值 `publishCart()` 進來就好：數字仍然來自伺服器，
 * 不是前端自己 +1 猜出來的，也就不會跟真實購物車漂移。
 */
import type { components } from '@greygray/api-client/storefront';
import { countCartItems, type CartItemCount } from './cartBadge';

type S = components['schemas'];

let snapshot: CartItemCount = null;
const listeners = new Set<() => void>();

/** `useSyncExternalStore` 的 `getSnapshot`。 */
export function getCartItemCount(): CartItemCount {
  return snapshot;
}

/**
 * `useSyncExternalStore` 的 `getServerSnapshot`。**永遠是「不知道」。**
 * 伺服器端不曉得這個瀏覽器的購物車，猜一個數字既是說謊，也會造成 hydration 不一致。
 */
export function getServerCartItemCount(): CartItemCount {
  return null;
}

/** 直接設定件數。值沒變就不通知，避免無謂的 re-render。 */
export function setCartItemCount(next: CartItemCount): void {
  if (next === snapshot) return;
  snapshot = next;
  // 走複本：listener 在被呼叫時退訂（React 卸載）不會弄壞這一輪迭代。
  for (const listener of [...listeners]) listener();
}

/** 拿一份伺服器回來的 `Cart` 更新徽章。這是呼叫端唯一該用的入口。 */
export function publishCart(cart: S['Cart'] | null | undefined): void {
  setCartItemCount(countCartItems(cart));
}

/** 訂閱；回傳退訂函式。 */
export function subscribeCartItemCount(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** **只給測試用。** 模組層級的狀態會跨 test case 殘留。 */
export function resetCartItemCountStore(): void {
  snapshot = null;
  listeners.clear();
}
