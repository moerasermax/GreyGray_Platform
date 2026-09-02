/**
 * 登出的**順序**：呼叫後端 → 徽章歸零 → 回首頁。
 *
 * ── 為什麼抽成一支純函式 ──
 * 這個 workspace 沒有 jsdom 也沒有 `@testing-library`，元件裡的 `handleLogout`
 * 測不到。而這裡要釘住的正好是一條「順序與分支」的規則，不是畫面：
 * **成功才歸零，失敗不動。**
 *
 * ── 為什麼失敗時徽章不動 ──
 * 登出失敗代表 session cookie 還在，購物車也還在。把徽章清成空的
 * 等於畫面講了一句不成立的話（`_lib/cartBadge.ts` 開頭那條規則），
 * 而使用者可能正在別人的手機上按這顆按鈕——他需要知道自己**沒有**登出成功。
 *
 * ── 為什麼要有這一步（#36）──
 * 後端 BE-41 之後，logout 會同時刪掉 `gg_cart` cookie，下一次 `GET /v1/cart`
 * 就是一台空車。但徽章是模組 store 裡的數字，沒有人重新發布它就會停在登出前的件數——
 * 使用者看到的是「已經登出，購物車卻還有 3 件」。
 * `publishCart(null)` ＝「不知道幾件」＝ 不畫徽章（不是畫一個 0）。
 */
import { publishCart } from '../../_lib/cartCountStore';

export interface LogoutEffects {
  /** 打後端的登出端點（含冪等鍵）。丟例外＝失敗。 */
  logout(): Promise<unknown>;
  /** 登出成功之後去哪。元件傳的是 `router.replace('/')`。 */
  goHome(): void;
}

/** 回 `true` 代表登出成功（徽章已歸零並導向）；`false` 代表失敗，什麼都沒動。 */
export async function performLogout(effects: LogoutEffects): Promise<boolean> {
  try {
    await effects.logout();
  } catch {
    return false;
  }
  publishCart(null);
  effects.goHome();
  return true;
}
