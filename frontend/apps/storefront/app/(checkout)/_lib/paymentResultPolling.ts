/**
 * 付款結果頁「自動重查」的排程。
 *
 * ── 為什麼需要重查 ──
 * 訂單從 `AwaitingPayment` 變成 `PaidAwaitingClose` 不是付款當下發生的：
 * 綠界回呼進來 → 寫 outbox → Worker 非同步處理。瀏覽器從收銀台按「返回商店」
 * 回到這一頁時多半只過了一秒，Worker 還沒跑到，頁面就會顯示「尚未確認付款」。
 * **人剛剛才刷完卡，看到這五個字會以為付款失敗**——然後去重刷一次。
 *
 * ── 為什麼是有限次而不是輪詢到好 ──
 * 「等到成功為止」在正常情況下看起來最好，在異常情況下最糟：付款真的失敗、
 * 或 Worker 掛了的時候，那個分頁會一直打 API 打到使用者關掉它為止，
 * 而畫面永遠停在「正在確認」——一個**永遠不會結束、也永遠不給人下一步**的畫面。
 * 所以排程是寫死的五次、19 秒內結束，用完就停下來把控制權還給使用者
 * （頁面顯示「重新查詢」按鈕，按不按由他決定）。
 *
 * 抽成純函式住在這裡的理由與 `_lib/tabs.ts`、`_lib/topBar.ts` 相同：
 * 這個 workspace 沒有 jsdom，元件裡的邏輯測不到，抽出來才測得到。
 */
import type { components } from '@greygray/api-client/storefront';

type OrderStatus = components['schemas']['OrderStatus'];

/**
 * 第 n 次重查要等多久（毫秒）。**遞增**：剛回來的那一兩秒最可能已經處理完，
 * 之後拉長間隔，避免在後端本來就慢的時候再壓上去。
 *
 * 長度就是重查次數的上限。總和 19 秒——比使用者願意盯著一個「正在確認」的畫面
 * 看的時間略長一點，再久就該讓他自己決定要不要再查。
 */
export const POLL_DELAYS_MS: readonly number[] = [1000, 2000, 3000, 5000, 8000];

/** 排程總共會花多久。給測試與說明用，不參與判斷。 */
export const POLL_TOTAL_MS: number = POLL_DELAYS_MS.reduce((sum, delay) => sum + delay, 0);

/**
 * 只有「還在等付款」這一個狀態需要重查。
 *
 * 付成功了、取消了、或任何其他狀態都是**終局**——後端已經給了答案，
 * 再問一次只會拿到同一個答案。
 */
const POLLABLE_STATUS: OrderStatus = 'AwaitingPayment';

/**
 * 還要不要再排下一次重查。
 *
 * @param status 剛剛那一次查詢拿到的訂單狀態。
 * @param attempt 已經重查過幾次（第一次自動重查時是 0）。
 */
export function shouldKeepPolling(status: OrderStatus, attempt: number): boolean {
  if (status !== POLLABLE_STATUS) return false;
  if (!Number.isInteger(attempt) || attempt < 0) return false;
  return attempt < POLL_DELAYS_MS.length;
}

/**
 * 第 `attempt` 次重查前要等的毫秒數；排程用完回 `null`。
 *
 * 回 `null` 而不是回 0 或最後一個值，是因為呼叫端要分得出
 * 「等這麼久」與「不要再等了」——後者要顯示「重新查詢」按鈕。
 */
export function pollDelayMs(attempt: number): number | null {
  if (!Number.isInteger(attempt) || attempt < 0) return null;
  return POLL_DELAYS_MS[attempt] ?? null;
}
