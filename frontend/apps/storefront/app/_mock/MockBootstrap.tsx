'use client';

import { useEffect } from 'react';

/*
 * 瀏覽器端的 mock 掛載點。
 *
 * ── 為什麼不擋住 children ──
 * 常見寫法是「worker 起來之前先 return null」，但那會讓 mock 開著的時候整頁沒有
 * SSR 內容——而 FE-3 的驗收條件正是 `view-source` 要看得到商品名。
 * SSR 那一側已經由 `instrumentation.ts` 用 `setupServer` 攔掉了，
 * 首屏資料本來就是 mock 的，所以這裡不需要擋，直接讓 children 照常渲染。
 *
 * 代價是：掛載完成前送出的 client 端請求會漏掉攔截。M1a 的頁面資料都走 SSR，
 * 真正的 client 端請求（加入購物車、收藏）都要等使用者互動，那時 worker 早就好了。
 *
 * ── service worker 檔案 ──
 * `public/mockServiceWorker.js` 是 `msw init` 產生的，已經進版控。
 * **升級 msw 之後要重跑**，否則版本對不上 worker 會拒絕啟動：
 *   pnpm --filter @greygray/api-client exec msw init ../../apps/storefront/public
 */

let started: Promise<unknown> | null = null;

function startOnce() {
  if (started) return started;
  started = (async () => {
    const [{ isMockEnabled }, { storefrontWorker }] = await Promise.all([
      import('@greygray/api-client/mock/env'),
      import('@greygray/api-client/mock/browser'),
    ]);
    if (!isMockEnabled()) return;
    await storefrontWorker.start({
      // 沒有 handler 的請求（Next.js 的 HMR、字型、圖片）一律放行，不要吵。
      onUnhandledRequest: 'bypass',
      quiet: false,
    });
    // eslint-disable-next-line no-console
    console.info('[mock] storefront worker 已啟動（NEXT_PUBLIC_USE_MOCK=1）');
  })();
  return started;
}

export function MockBootstrap() {
  useEffect(() => {
    if (process.env['NEXT_PUBLIC_USE_MOCK'] !== '1') return;
    void startOnce();
  }, []);

  return null;
}
