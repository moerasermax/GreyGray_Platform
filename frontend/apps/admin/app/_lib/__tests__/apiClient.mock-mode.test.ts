import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { NextRequest } from 'next/server';
import { ADMIN_BASE_URL as ADMIN_HANDLER_BASE_URL } from '@greygray/api-client/mock/handlers.admin';
import { ADMIN_BASE_URL as ADMIN_AUTH_BASE_URL } from '@greygray/api-client/mock/handlers.admin.auth';
import { adminServer } from '../../_mock/server.node';

const mockBootstrapState = vi.hoisted(() => ({ loaded: false }));

vi.mock('next/headers', () => ({
  cookies: async () => ({ getAll: () => [] }),
}));

vi.mock('../../_mock/MockBootstrap', () => {
  mockBootstrapState.loaded = true;
  return { startMock: vi.fn(async () => undefined) };
});

beforeAll(() => adminServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  adminServer.resetHandlers();
  vi.unstubAllEnvs();
  vi.resetModules();
  mockBootstrapState.loaded = false;
});
afterAll(() => adminServer.close());

describe('admin apiClient mock mode', () => {
  it('mock 模式固定使用 handler 的 localhost 網址', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5001');
    vi.resetModules();

    const { serverApi } = await import('../apiClient');
    const api = await serverApi();

    await expect(api.get('/v1/me')).resolves.toBeDefined();
  });

  it('非 mock 模式維持 NEXT_PUBLIC_API_BASE_URL', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '0');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5001');
    adminServer.use(
      http.get('http://127.0.0.1:5001/v1/probe', () => HttpResponse.json({ source: 'env' })),
    );
    vi.resetModules();

    const { serverApi } = await import('../apiClient');
    const api = await serverApi();

    await expect(api.get<{ source: string }>('/v1/probe')).resolves.toEqual({ source: 'env' });
  });

  it('mock 網址字面值與兩份 admin handler 一致', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5001');
    vi.resetModules();

    const { MOCK_API_BASE_URL } = await import('../apiClient');

    expect(MOCK_API_BASE_URL).toBe(ADMIN_HANDLER_BASE_URL);
    expect(MOCK_API_BASE_URL).toBe(ADMIN_AUTH_BASE_URL);
  });

  it('node 環境不載入 MockBootstrap', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5001');
    vi.resetModules();

    await import('../apiClient');
    await vi.dynamicImportSettled();

    expect(mockBootstrapState.loaded).toBe(false);
  });
});

describe('admin middleware mock mode', () => {
  it('mock 模式沒有 cookie 也放行 orders', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.resetModules();

    const { middleware } = await import('../../../middleware');
    const response = middleware(new NextRequest('https://admin.example/orders'));

    expect(response.headers.get('x-middleware-next')).toBe('1');
  });

  it('非 mock 模式沒有 cookie 一定導向 login', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '0');
    vi.resetModules();

    const { middleware } = await import('../../../middleware');
    const response = middleware(new NextRequest('https://admin.example/orders?tab=open'));

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toMatch(/\/login\?from=%2Forders%3Ftab%3Dopen$/);
  });

  it('非 mock 模式有 cookie 維持放行', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '0');
    vi.resetModules();

    const request = new NextRequest('https://admin.example/orders', {
      headers: { cookie: 'gg_admin_session=session-token' },
    });
    const { middleware } = await import('../../../middleware');
    const response = middleware(request);

    expect(response.headers.get('x-middleware-next')).toBe('1');
  });
});
