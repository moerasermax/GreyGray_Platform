/**
 * 暫時的登入狀態管理。
 *
 * FE-1 的 mock server（MSW）跟 `src/endpoints/admin.ts` 還沒做出來，
 * 真正串接時這整份檔案要換成打 `POST /v1/auth/login`／`GET /v1/me`／`POST /v1/auth/logout`
 * （契約見 `docs/api/openapi.admin.yaml`，session 是 `gg_admin_session` 這個 HttpOnly cookie，
 * 前端本來就讀不到，屆時角色會來自 `GET /v1/me` 而不是這裡的假登入）。
 *
 * 這裡只是讓 FE-6 的殼、選單角色判斷、儀表板能先跑起來。
 */
import type { components } from '@greygray/api-client/admin';

export type Staff = components['schemas']['Staff'];
export type StaffRole = components['schemas']['StaffRole'];

const SESSION_STORAGE_KEY = 'gg-admin-mock-session';
const MOCK_LATENCY_MS = 500;

interface StaffFixture extends Staff {
  readonly password: string;
}

/** 四個角色各一組帳號，密碼都一樣，方便切角色測選單顯示。 */
export const STAFF_FIXTURES: readonly StaffFixture[] = [
  {
    id: '01926a1e000000000000000000a001',
    displayName: '林郁宸（Owner）',
    email: 'owner@greygray.tw',
    role: 'Owner',
    password: 'greygray123',
  },
  {
    id: '01926a1e000000000000000000a002',
    displayName: '陳小記（Accountant）',
    email: 'accountant@greygray.tw',
    role: 'Accountant',
    password: 'greygray123',
  },
  {
    id: '01926a1e000000000000000000a003',
    displayName: '王小理（Operator）',
    email: 'operator@greygray.tw',
    role: 'Operator',
    password: 'greygray123',
  },
  {
    id: '01926a1e000000000000000000a004',
    displayName: '張小看（ReadOnly）',
    email: 'readonly@greygray.tw',
    role: 'ReadOnly',
    password: 'greygray123',
  },
];

/**
 * 角色高低排序，對應 `openapi.admin.yaml` 裡 `StaffRole` 的說明：
 * `Owner > Accountant ≈ Operator > ReadOnly`。
 * **這只是前端選單顯示用的判斷，不是真正的授權**——後端的 Policy 才是最終防線。
 */
const ROLE_RANK: Record<StaffRole, number> = {
  ReadOnly: 0,
  Operator: 1,
  Accountant: 1,
  Owner: 2,
};

export function hasRequiredRole(current: StaffRole, required: StaffRole): boolean {
  return ROLE_RANK[current] >= ROLE_RANK[required];
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

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export class LoginError extends Error {}

export async function login(email: string, password: string): Promise<Staff> {
  await delay(MOCK_LATENCY_MS);

  const match = STAFF_FIXTURES.find(
    (staff) => staff.email?.toLowerCase() === email.trim().toLowerCase() && staff.password === password,
  );

  // 帳號不存在跟密碼錯一律同一句話，不送人一份帳號列舉工具。
  if (!match) {
    throw new LoginError('帳號或密碼錯誤');
  }

  const { password: _password, ...staff } = match;
  if (typeof window !== 'undefined') {
    window.localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(staff));
  }
  return staff;
}

export async function logout(): Promise<void> {
  await delay(200);
  if (typeof window !== 'undefined') {
    window.localStorage.removeItem(SESSION_STORAGE_KEY);
  }
}

export function getSession(): Staff | null {
  if (typeof window === 'undefined') return null;
  const raw = window.localStorage.getItem(SESSION_STORAGE_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Staff;
  } catch {
    return null;
  }
}
