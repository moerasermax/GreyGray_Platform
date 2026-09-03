/** 把 `ApiError` / `NetworkError` / 其他例外轉成一句可以直接顯示的中文訊息。 */
import { ApiError } from '@greygray/api-client';

export function apiErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.problem.detail ? `${error.problem.title}（${error.problem.detail}）` : error.problem.title;
  }
  if (error instanceof Error) return error.message;
  return '發生未預期的錯誤，請稍後再試。';
}

/**
 * 422 的逐欄位錯誤（`ProblemDetails.errors`）。key 是**契約的欄位名**（`weightGram`、`size`…），
 * 不是畫面上的欄位 id——呈現的一方負責對回自己的欄位。非 422／沒有 errors 時是空物件。
 */
export function apiFieldErrors(error: unknown): Record<string, string[]> {
  return error instanceof ApiError ? error.fieldErrors : {};
}

/** 把某一欄的錯誤接成一句話；那一欄沒有錯誤時回 `null`，可以直接餵 `Field` 的 `error`。 */
export function fieldErrorText(errors: Record<string, string[]>, field: string): string | null {
  const messages = errors[field];
  return messages && messages.length > 0 ? messages.join('　') : null;
}

export function apiErrorTraceId(error: unknown): string | null {
  return error instanceof ApiError ? error.shortTraceId : null;
}
