/**
 * 瀏覽器端進入點。**掛載不歸這一包**——`worker.start()` 要在 `app/layout.tsx`
 * （或專屬的 client bootstrap 元件）裡呼叫，且瀏覽器需要 `public/mockServiceWorker.js`：
 *
 * ```bash
 * pnpm --filter @greygray/api-client exec msw init ../../apps/storefront/public --save
 * pnpm --filter @greygray/api-client exec msw init ../../apps/admin/public --save
 * ```
 *
 * 整合方式（給掛載的人）：
 *
 * ```ts
 * import { isMockEnabled } from '@greygray/api-client/mock/env';
 * import { storefrontWorker } from '@greygray/api-client/mock/browser';
 *
 * if (isMockEnabled()) {
 *   await storefrontWorker.start({ onUnhandledRequest: 'bypass' });
 * }
 * ```
 *
 * `apps/admin` 用 `adminWorker`；如果同一個瀏覽器頁面同時要攔兩個 BFF（不建議），用 `worker`。
 */

import { setupWorker } from 'msw/browser';
import { adminHandlers } from './handlers.admin';
import { storefrontHandlers } from './handlers.storefront';

export const storefrontWorker = setupWorker(...storefrontHandlers);
export const adminWorker = setupWorker(...adminHandlers);
export const worker = setupWorker(...storefrontHandlers, ...adminHandlers);
