/**
 * 商品頁的小計預覽（單價 × 數量）。
 *
 * **這是 ADR-033 的唯一例外，不是新規則。** 前端四條之二是「不做金額運算」
 * （`frontend/README.md`）：加總、分攤、含運總額一律由後端回。這裡開的例外只有
 * 一個用途——商品詳情頁還沒進購物車，後端沒有端點能回「這個數量的小計」，
 * 而使用者選了 5 件卻看不到總額。
 *
 * 所以這支函式：
 * - **只用於顯示**，算出來的值不送後端、不存任何地方；
 * - 實際金額**一律以購物車為準**（後端 `GET /v1/cart` 回的小計／含運）；
 * - 購物車、結帳、訂單頁**不准照抄**——那些頁面的金額仍全部來自後端。
 *
 * 全 repo 只有商品詳情頁一個呼叫端。要在別的地方用之前，先回頭改 ADR-033。
 */
import type { Money } from '@greygray/api-client';

/**
 * 單價 × 數量。同幣別、整數乘法。
 *
 * 輸入不合法時 **throw**，不回 0 湊數：小計是給人看著決定要不要買的數字，
 * 悄悄變成 NT$0 比整頁壞掉更難發現。
 *
 * @throws {RangeError} 數量不是 ≥ 1 的整數、金額不是 ≥ 0 的整數，或相乘後超出安全整數範圍。
 */
export function subtotalPreview(price: Money, quantity: number): Money {
  if (!Number.isInteger(quantity) || quantity < 1) {
    throw new RangeError(`subtotalPreview：數量必須是 ≥ 1 的整數，收到 ${quantity}`);
  }
  if (!Number.isInteger(price.amountMinor) || price.amountMinor < 0) {
    throw new RangeError(`subtotalPreview：金額最小單位必須是 ≥ 0 的整數，收到 ${price.amountMinor}`);
  }

  const amountMinor = price.amountMinor * quantity;
  if (!Number.isSafeInteger(amountMinor)) {
    throw new RangeError(`subtotalPreview：小計 ${amountMinor} 超出安全整數範圍`);
  }

  return { amountMinor, currency: price.currency };
}
