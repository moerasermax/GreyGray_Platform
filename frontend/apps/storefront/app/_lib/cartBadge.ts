/**
 * 購物車徽章上的數字。
 *
 * ── 唯一一條不可以違反的規則 ──
 * **「不知道幾件」不可以被畫成 0。** 這個專案一再踩到的失敗形狀就是
 * 「畫面宣稱了不成立的事」（#29 後台首頁的假數字、FE-21 那顆只跳成功提示卻不重新取數的按鈕），
 * 一顆在 API 掛掉時仍然理直氣壯顯示 0 的徽章屬於同一類。
 * 所以計數的型別是 `number | null`，`null` 就是「還不知道」，
 * 而 `null` 在畫面上的結果是**沒有徽章**，不是 0。
 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

/** `null` = 還不知道有幾件（沒問過、或問了失敗）。**不要拿 0 代替它。** */
export type CartItemCount = number | null;

/**
 * 購物車裡有幾件。**取的是各行 `quantity` 的總和，不是 `lines.length`。**
 *
 * ── 這是裁決過的，不要改回品項數 ──
 * 派工書 §1 的字面是「`GET /v1/cart` 的品項數」，第一版照字面做了 `lines.length`，
 * 交付時提報，整合者裁決改成數量總和。**決定性的理由是同一個檔案裡的
 * `cartTabAccessibleName`**：它唸的是「購物車，X 件」。
 * 購物車有一行、`quantity = 5` 時，唸成「1 件」是**錯的**——使用者手上有 5 件。
 * 那句話會講出不成立的事，正好違反這個檔案開頭那條「不可以違反的規則」。
 * 「品項數」與「件數」是兩個名詞，而畫面與語音上我們講的是**件數**。
 *
 * ── 依據：後端會把同一個 SKU 的重複加入併進同一行（對 dev 後端實測）──
 * `POST /v1/cart/lines` 加 SKU-A×2 之後再加 SKU-A×3，
 * 回來的是**一行、`quantity = 5`**，不是兩行。所以兩種取法差在：
 *
 *     加 A×2 → 再加 A×3 → 加 B×1
 *     lines.length（舊）： 1 → 1 → 2     ← 中間那一步徽章一動也不動
 *     數量總和（現在）：   2 → 5 → 6
 *
 * 舊取法在中間那一步會讓使用者看到「已加入購物車」卻發現徽章沒反應——
 * 那是這個專案一再踩到的「按了沒反應」形狀，也違反派工書
 * 「加入成功之後徽章必須立刻更新」的用意。
 * `cartBadge.test.ts` 有一條專門釘住這個裁決的測試，改回 `lines.length` 會紅。
 *
 * ── 不可信的資料一律回 `null`，不回 `NaN` ──
 * 只要有一行的 `quantity` 不是有限的非負數，**整個數字就不可信，回 `null`**。
 * 不用 `reduce` 直接加是刻意的：`undefined` 會讓總和變成 `NaN`、
 * 字串會變成字串串接，而**徽章上出現 `NaN` 比顯示 0 還糟**。
 * 也不選「跳過壞掉那一行」——那會安靜地少算，等於畫出一個不成立的數字。
 *
 * 拿不到購物車（`null`／`undefined`／形狀不對）一律回 `null`＝不知道。
 */
export function countCartItems(cart: S['Cart'] | null | undefined): CartItemCount {
  if (!cart || !Array.isArray(cart.lines)) return null;

  let total = 0;
  for (const cartLine of cart.lines) {
    const quantity: unknown = cartLine?.quantity;
    if (typeof quantity !== 'number' || !Number.isFinite(quantity) || quantity < 0) return null;
    total += quantity;
  }
  return total;
}

/**
 * 徽章上要印的字。回 `null` 代表**不要畫徽章**。
 *
 * 三種情況都不畫：不知道（`null`）、真的空車（0）、以及不該出現的負數。
 * 空車不畫是因為徽章的作用是「提醒你有東西」，一個 0 只是雜訊；
 * 而「空車」與「不知道」在**可及名稱**上仍然分得出來，見 `cartTabAccessibleName`。
 * 超過 99 收成 `99+`，否則兩位數以上的徽章會把分頁擠歪。
 */
export function formatCartBadge(count: CartItemCount): string | null {
  if (count === null) return null;
  if (!Number.isFinite(count) || count <= 0) return null;
  return count > 99 ? '99+' : String(count);
}

/**
 * 購物車分頁的可及名稱（螢幕閱讀器唸出來的那一句）。
 *
 * **這裡才是「空車」與「不知道」真正分家的地方**：畫面上兩者都沒有徽章，
 * 但唸出來一個是「目前是空的」、一個只有「購物車」。
 * 不知道的時候不會被講成「0 件」。
 */
export function cartTabAccessibleName(count: CartItemCount): string {
  if (count === null || !Number.isFinite(count) || count < 0) return '購物車';
  return count === 0 ? '購物車，目前是空的' : `購物車，${count} 件`;
}
