/**
 * Node 端進入點（SSR、vitest）。**前台商品頁走 SSR（FE-3），這條路徑不能省**——
 * server component 裡的 `fetch` 不會經過瀏覽器的 Service Worker。
 *
 * 整合方式（給掛載的人，通常是 Next.js 的 `instrumentation.ts`）：
 *
 * ```ts
 * export async function register() {
 *   if (process.env.NEXT_RUNTIME === 'nodejs' && process.env.NEXT_PUBLIC_USE_MOCK === '1') {
 *     const { storefrontServer } = await import('@greygray/api-client/mock/server');
 *     storefrontServer.listen({ onUnhandledRequest: 'bypass' });
 *   }
 * }
 * ```
 *
 * 測試（vitest）裡直接 `server.listen()` / `server.resetHandlers()` / `server.close()`，
 * 用法見 `mock/__tests__/`。
 */

import { setupServer } from 'msw/node';
import { adminHandlers } from './handlers.admin';
import { storefrontHandlers } from './handlers.storefront';

export const storefrontServer = setupServer(...storefrontHandlers);
export const adminServer = setupServer(...adminHandlers);
export const server = setupServer(...storefrontHandlers, ...adminHandlers);
