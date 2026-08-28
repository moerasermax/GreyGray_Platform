import { ApiError } from '@greygray/api-client';

/** 需要登入的頁面直接用自己那支端點的 401 判斷要不要導去登入頁，不用另外多打一次 `/v1/me`。 */
export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.isUnauthorized;
}
