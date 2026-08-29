/** 把 `ApiError` / `NetworkError` / 其他例外轉成一句可以直接顯示的中文訊息。 */
import { ApiError } from '@greygray/api-client';

export function apiErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.problem.detail ? `${error.problem.title}（${error.problem.detail}）` : error.problem.title;
  }
  if (error instanceof Error) return error.message;
  return '發生未預期的錯誤，請稍後再試。';
}

export function apiErrorTraceId(error: unknown): string | null {
  return error instanceof ApiError ? error.shortTraceId : null;
}
