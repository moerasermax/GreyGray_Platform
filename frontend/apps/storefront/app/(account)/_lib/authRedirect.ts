import { ApiError } from '@greygray/api-client';
import { DEFAULT_NEXT, currentNext, loginHref } from '../../_lib/auth';

/** 需要登入的頁面直接用自己那支端點的 401 判斷要不要導去登入頁，不用另外多打一次 `/v1/me`。 */
export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthorized;
}

/**
 * 被 401 彈走時要去的登入頁網址——**帶著現在這一頁當回程**。
 *
 * ── 為什麼讀 `window.location` 而不是 `usePathname()` ＋ `useSearchParams()` ──
 * 這個函式只在「API 回了 401」的 callback 裡被呼叫，那一定已經 mount 過了，
 * 讀 `window` 不會有 SSR 或 hydration 的問題。反過來走 hook 的話，
 * 四個需要登入的頁面每一頁都得為了 `useSearchParams()` 多包一層 Suspense
 * ——為了一個只在錯誤路徑上用得到的值改四頁的結構，代價不對。
 *
 * 伺服器端（理論上到不了這裡）回預設目標而不是丟例外：判斷失準的後果
 * 應該是「登入完到 `/me`」，不是整頁炸掉。
 */
export function loginHrefForCurrentPage(): string {
  if (typeof window === 'undefined') return loginHref(DEFAULT_NEXT);
  return loginHref(currentNext(window.location.pathname, window.location.search));
}
