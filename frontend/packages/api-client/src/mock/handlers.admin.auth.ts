/**
 * Admin BFF 的 auth mock：`POST /v1/auth/login`、`POST /v1/auth/logout`、`GET /v1/me`。
 *
 * `handlers.admin.ts`（FE-9 的檔案）已經有這三個路徑的 stub（無條件回同一個 `staffFixture`），
 * 陣列裡先比對到的 handler 會贏，所以這裡的 `adminAuthHandlers` 目前**還沒有真的生效**——
 * 要等 `handlers.admin.ts` 那三行 stub 被移除才會接上。詳情見交付報告，不在這支檔案自己動手改，
 * 那個檔案不是這一包的。
 *
 * session 用 `gg_admin_session` 這個 HttpOnly cookie 模擬：登入成功用 `Set-Cookie` 發一把
 * token，之後的請求靠同一把 cookie 換回員工資料。四個角色的測試帳號**只准留在這裡**，
 * 不准出現在正式碼路徑。
 */

import { http, HttpResponse } from 'msw';
import type { components } from '../types.admin';
import { hexId } from './ids';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

export const ADMIN_BASE_URL = 'http://localhost:5001';

function url(path: string): string {
  return `${ADMIN_BASE_URL}${path}`;
}

const SESSION_COOKIE = 'gg_admin_session';

type StaffFixture = S['Staff'] & { readonly password: string };

/** 四個角色各一組帳號，密碼都一樣，方便切角色測選單顯示。**只准留在 mock 裡。** */
export const STAFF_FIXTURES: readonly StaffFixture[] = [
  {
    id: hexId('admin-staff:owner'),
    displayName: '林郁宸（Owner）',
    email: 'owner@greygray.tw',
    role: 'Owner',
    password: 'greygray123',
  },
  {
    id: hexId('admin-staff:accountant'),
    displayName: '陳小記（Accountant）',
    email: 'accountant@greygray.tw',
    role: 'Accountant',
    password: 'greygray123',
  },
  {
    id: hexId('admin-staff:operator'),
    displayName: '王小理（Operator）',
    email: 'operator@greygray.tw',
    role: 'Operator',
    password: 'greygray123',
  },
  {
    id: hexId('admin-staff:readonly'),
    displayName: '張小看（ReadOnly）',
    email: 'readonly@greygray.tw',
    role: 'ReadOnly',
    password: 'greygray123',
  },
];

// ── 可變狀態：token → 員工。測試之間要 reset。 ──────────────────────────────

let sessions = new Map<string, S['Staff']>();

/** 測試之間重置 mock 的可變狀態。 */
export function resetAdminAuthMockState(): void {
  sessions = new Map();
}

function readSessionToken(request: Request): string | null {
  const raw = request.headers.get('cookie');
  if (!raw) return null;
  const found = raw
    .split(';')
    .map((part) => part.trim())
    .find((part) => part.startsWith(`${SESSION_COOKIE}=`));
  return found ? found.slice(SESSION_COOKIE.length + 1) : null;
}

function sessionCookieHeader(token: string | null): string {
  if (token === null) {
    return `${SESSION_COOKIE}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0`;
  }
  return `${SESSION_COOKIE}=${token}; Path=/; HttpOnly; SameSite=Lax`;
}

export const adminAuthHandlers = [
  http.post(url('/v1/auth/login'), async ({ request }) => {
    let body: { email?: unknown; password?: unknown };
    try {
      body = (await request.json()) as { email?: unknown; password?: unknown };
    } catch {
      return jsonProblem(problem(400, 'platform.malformed-request', '請求格式錯誤。'));
    }
    const email = typeof body.email === 'string' ? body.email.trim().toLowerCase() : '';
    const password = typeof body.password === 'string' ? body.password : '';
    // 帳號不存在跟密碼錯一律同一句話，不送人一份帳號列舉工具。
    const match = STAFF_FIXTURES.find((staff) => staff.email?.toLowerCase() === email && staff.password === password);
    if (!match) {
      return jsonProblem(problem(401, 'auth.invalid-credentials', '帳號或密碼錯誤。'));
    }
    const { password: _password, ...staff } = match;
    const token = hexId(`admin-session:${staff.id}:${Date.now()}:${Math.random()}`);
    sessions.set(token, staff);
    return HttpResponse.json(staff, { headers: { 'Set-Cookie': sessionCookieHeader(token) } });
  }),

  http.post(url('/v1/auth/logout'), ({ request }) => {
    const token = readSessionToken(request);
    if (token) sessions.delete(token);
    return new HttpResponse(null, { status: 204, headers: { 'Set-Cookie': sessionCookieHeader(null) } });
  }),

  http.get(url('/v1/me'), ({ request }) => {
    const token = readSessionToken(request);
    const staff = token ? sessions.get(token) : undefined;
    if (!staff) {
      return jsonProblem(problem(401, 'auth.session-expired', '請重新登入。'));
    }
    return HttpResponse.json(staff);
  }),
];
