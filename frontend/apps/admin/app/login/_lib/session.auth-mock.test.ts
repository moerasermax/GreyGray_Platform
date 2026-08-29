/**
 * `handlers.admin.ts`（FE-9 的檔案）現有的三行 auth stub 會在 MSW 的陣列比對裡
 * 先擋下 `adminAuthHandlers`（先比對到的 handler 贏），所以沒辦法透過共用的
 * `adminServer` 驗到「帳密錯誤回 401」「session 過期回 401」這些行為。
 *
 * 這裡直接對 `adminAuthHandlers` 開一個獨立的 `setupServer`，繞過那個還沒解決的
 * 排序問題，驗證 mock 本身的行為是對的：登入成功發 cookie、`GET /v1/me` 認 cookie、
 * 密碼錯誤與沒帶 cookie 都回 401、登出後 cookie 失效。
 */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { setupServer } from 'msw/node';
import { ApiClient } from '@greygray/api-client';
import { ApiError } from '@greygray/api-client';
import { getMe, login, logout } from '@greygray/api-client/endpoints/admin';
import { ADMIN_BASE_URL, STAFF_FIXTURES, adminAuthHandlers, resetAdminAuthMockState } from '@greygray/api-client/mock/handlers.admin.auth';

const authServer = setupServer(...adminAuthHandlers);
let seq = 0;
const opts = () => ({ idempotencyKey: `auth-mock-test-${++seq}` });

beforeAll(() => authServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  authServer.resetHandlers();
  resetAdminAuthMockState();
});
afterAll(() => authServer.close());

function client(): ApiClient {
  return new ApiClient({ baseUrl: ADMIN_BASE_URL });
}

describe('adminAuthHandlers', () => {
  it('帳密正確：登入成功、GET /v1/me 拿得回同一個人', async () => {
    const c = client();
    const owner = STAFF_FIXTURES.find((f) => f.role === 'Owner')!;
    const staff = await login(c, { email: owner.email!, password: owner.password }, opts());
    expect(staff.role).toBe('Owner');

    const me = await getMe(c);
    expect(me.id).toBe(staff.id);
  });

  it('密碼錯誤回 401（auth.invalid-credentials）', async () => {
    const c = client();
    expect.assertions(2);
    try {
      await login(c, { email: 'owner@greygray.tw', password: '不對的密碼' }, opts());
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).code).toBe('auth.invalid-credentials');
    }
  });

  it('沒登入就打 GET /v1/me 回 401（auth.session-expired）', async () => {
    const c = client();
    expect.assertions(2);
    try {
      await getMe(c);
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).code).toBe('auth.session-expired');
    }
  });

  it('登出後同一個 client 再打 GET /v1/me 要回 401', async () => {
    const c = client();
    const operator = STAFF_FIXTURES.find((f) => f.role === 'Operator')!;
    await login(c, { email: operator.email!, password: operator.password }, opts());
    await getMe(c);

    await logout(c, opts());

    expect.assertions(1);
    try {
      await getMe(c);
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
    }
  });

  it('四個角色各自登入都拿回對應的角色', async () => {
    for (const fixture of STAFF_FIXTURES) {
      const c = client();
      const staff = await login(c, { email: fixture.email!, password: fixture.password }, opts());
      expect(staff.role).toBe(fixture.role);
    }
  });
});
