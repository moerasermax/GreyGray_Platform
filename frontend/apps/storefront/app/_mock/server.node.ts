/*
 * SSR 端的 msw server。**`setupServer` 一定要在 app 自己這裡建，不能從
 * `@greygray/api-client/mock/server` 匯入**——那個套件列在 `transpilePackages` 裡，
 * 會被 webpack 用瀏覽器條件解析，而 `@mswjs/interceptors` 的 `./ClientRequest`
 * 在 browser 條件下是 `null`，dev 會直接 500（build 反而看不出來）。
 *
 * 從 app 直接 import `msw/node` 才吃得到 `next.config.ts` 的 `serverExternalPackages`。
 * handlers 本身是純 msw 宣告，跨執行環境安全，繼續從套件拿。
 */
import { setupServer } from 'msw/node';
import { storefrontHandlers } from '@greygray/api-client/mock/handlers.storefront';

export const storefrontServer = setupServer(...storefrontHandlers);
