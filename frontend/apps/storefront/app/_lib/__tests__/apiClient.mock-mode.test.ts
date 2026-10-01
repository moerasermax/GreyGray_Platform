import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { STOREFRONT_BASE_URL } from '@greygray/api-client/mock/handlers.storefront';
import { storefrontServer } from '../../_mock/server.node';

const mockBootstrapState = vi.hoisted(() => ({ loaded: false }));

vi.mock('next/headers', () => ({
  cookies: async () => ({ getAll: () => [] }),
}));

vi.mock('../../_mock/MockBootstrap', () => {
  mockBootstrapState.loaded = true;
  return { startMock: vi.fn(async () => undefined) };
});

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  storefrontServer.resetHandlers();
  vi.unstubAllEnvs();
  vi.resetModules();
  mockBootstrapState.loaded = false;
});
afterAll(() => storefrontServer.close());

describe('storefront apiClient mock mode', () => {
  it('mock 模式固定使用 handler 的 localhost 網址', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5000');
    vi.resetModules();

    const { serverApi } = await import('../apiClient');
    const api = await serverApi();

    await expect(api.get('/v1/categories')).resolves.toBeDefined();
  });

  it('非 mock 模式維持 NEXT_PUBLIC_API_BASE_URL', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '0');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5000');
    storefrontServer.use(
      http.get('http://127.0.0.1:5000/v1/probe', () => HttpResponse.json({ source: 'env' })),
    );
    vi.resetModules();

    const { serverApi } = await import('../apiClient');
    const api = await serverApi();

    await expect(api.get<{ source: string }>('/v1/probe')).resolves.toEqual({ source: 'env' });
  });

  it('mock 網址字面值與 storefront handler 一致', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5000');
    vi.resetModules();

    const { MOCK_API_BASE_URL } = await import('../apiClient');

    expect(MOCK_API_BASE_URL).toBe(STOREFRONT_BASE_URL);
  });

  it('node 環境不載入 MockBootstrap', async () => {
    vi.stubEnv('NEXT_PUBLIC_USE_MOCK', '1');
    vi.stubEnv('NEXT_PUBLIC_API_BASE_URL', 'http://127.0.0.1:5000');
    vi.resetModules();

    await import('../apiClient');
    await vi.dynamicImportSettled();

    expect(mockBootstrapState.loaded).toBe(false);
  });
});
