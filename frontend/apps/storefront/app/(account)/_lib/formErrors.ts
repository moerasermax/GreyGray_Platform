/**
 * 把 `ApiError.fieldErrors`（422 的 `errors`）轉成表單欄位可以直接用的錯誤字串。
 *
 * `Field` 元件的 `error` 只吃一個字串，後端一個欄位可能回多則訊息，這裡固定取第一則——
 * 客人一次只需要看一個理由就夠了，其餘的還在，重新送出會再檢查一次。
 */
import { ApiError } from '@greygray/api-client';

export function fieldErrorsFrom(error: unknown): Record<string, string> {
  if (!(error instanceof ApiError)) return {};
  const result: Record<string, string> = {};
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const first = messages[0];
    if (first) result[field] = first;
  }
  return result;
}

/** 沒有對應到任何欄位（或不是欄位驗證錯誤）時，用來顯示的整體錯誤訊息。 */
export function generalErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.problem.title;
  return '發生非預期的錯誤，請稍後再試。';
}

/** `ErrorState` 要顯示的 traceId 後 8 碼，讓客人能報給客服。不是 `ApiError` 就沒有。 */
export function traceIdOf(error: unknown): string | null {
  return error instanceof ApiError ? error.shortTraceId : null;
}
