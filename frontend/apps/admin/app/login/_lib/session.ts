/**
 * 後台登入狀態管理。打真的 BFF：`POST /v1/auth/login`、`GET /v1/me`、`POST /v1/auth/logout`
 * （契約見 `docs/api/openapi.admin.yaml`）。
 *
 * session 是 `gg_admin_session` 這個 HttpOnly cookie，前端 JS 讀不到——這是刻意的，
 * access token 永不進瀏覽器。**「有沒有登入」唯一的判斷方法是打 `GET /v1/me`**：
 * 200 是有、401 是沒有，這裡不維護一份「已登入」的布林值當真相。
 *
 * `getSession()` 是同步的：既有頁面（開團／商品／訂單等）散在各處用它同步讀角色，
 * 那些呼叫點不歸這一包管，這裡只能保留同步介面。它讀的是上一次成功打後端拿到的
 * 快取，不是真相本身；真的要問後端現在有沒有登入，呼叫 {@link refreshSession}。
 */
import { ApiError, newIdempotencyKey } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { getMe, login as loginRequest, logout as logoutRequest } from '@greygray/api-client/endpoints/admin';
import { browserApi } from '../../_lib/apiClient';

export type Staff = components['schemas']['Staff'];
export type StaffRole = components['schemas']['StaffRole'];

/**
 * 角色是否符合某個端點／畫面的要求。規則與後端 `IStaffRolePolicy.Allows`
 * （Identity 模組 `IdentityServices.cs` 的 `StaffRolePolicy`）**逐字相同**：
 *
 * 1. `Owner` 什麼都能做；
 * 2. 角色相同才通——**營運（Operator）與會計（Accountant）互不相通**；
 * 3. 要求 `ReadOnly` 的端點誰都能進。
 *
 * 刻意**不用等級大小比較**：凍結契約明寫「權限判斷不要用 enum 大小比較」，
 * 而 FE-50 之前的 `ROLE_RANK` 把營運與會計當同級，害會計看到出貨頁、營運看到帳務頁，
 * 一按就 403（派工書 `docs/52` 的實測表）。
 *
 * **這只是前端顯示與「要不要發請求」的判斷，不是真正的授權**——後端的 Policy 才是
 * 最終防線，這裡判斷錯了畫面會多顯示或少顯示一個東西，不會讓人多拿到權限。
 */
export function hasRequiredRole(current: StaffRole, required: StaffRole): boolean {
  return current === 'Owner' || current === required || required === 'ReadOnly';
}

export function roleLabel(role: StaffRole | (string & {})): string {
  switch (role) {
    case 'Owner':
      return '負責人';
    case 'Accountant':
      return '會計';
    case 'Operator':
      return '營運';
    case 'ReadOnly':
      return '唯讀';
    default:
      return role;
  }
}

export class LoginError extends Error {}

let cachedStaff: Staff | null = null;

/**
 * 同步讀上一次成功呼叫（`login` 或 `refreshSession`）留下的快取。
 * 瀏覽器硬重新整理後、還沒問過後端之前，這裡會是 `null`，即使 cookie 其實還有效。
 */
export function getSession(): Staff | null {
  return cachedStaff;
}

export async function login(email: string, password: string, idempotencyKey?: string): Promise<Staff> {
  try {
    const staff = await loginRequest(
      browserApi(),
      { email: email.trim(), password },
      { idempotencyKey: idempotencyKey ?? newIdempotencyKey() },
    );
    cachedStaff = staff;
    return staff;
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 401) {
      throw new LoginError(cause.problem.title || '帳號或密碼錯誤');
    }
    throw cause;
  }
}

export async function logout(): Promise<void> {
  try {
    await logoutRequest(browserApi(), { idempotencyKey: newIdempotencyKey() });
  } finally {
    cachedStaff = null;
  }
}

/**
 * 問後端「現在到底有沒有登入」，並更新 {@link getSession} 的快取。
 * 200 回真的員工資料、401 代表沒登入或 session 過期，兩種都回傳 `null`
 * （呼叫端要分辨「沒登入」與「session 過期」的話自己保留呼叫前後的狀態去比）。
 * 非 401 的例外一律往外丟，不要吞掉。
 */
export async function refreshSession(): Promise<Staff | null> {
  try {
    const staff = await getMe(browserApi());
    cachedStaff = staff;
    return staff;
  } catch (cause) {
    cachedStaff = null;
    if (cause instanceof ApiError && cause.status === 401) return null;
    throw cause;
  }
}
