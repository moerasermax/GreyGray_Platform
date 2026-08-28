'use client';

import { useEffect } from 'react';

/*
 * 瀏覽器端的 mock 掛載點。
 *
 * ── 為什麼不擋住 children ──
 * 常見寫法是「worker 起來之前先 return null」，但那會讓整頁在 mock 開著時空白一拍。
 * server component 那一側已經由 `instrumentation.ts` 用 `setupServer` 攔掉了，
 * 所以這裡不需要擋，直接讓 children 照常渲染。
 *
 * 代價是：掛載完成前送出的 client 端請求會漏掉攔截。後台的資料抓取都在
 * 使用者登入之後才發生，那時 worker 早就好了。
 *
 * ── service worker 檔案 ──
 * `public/mockServiceWorker.js` 是 `msw init` 產生的，已經進版控。
 * **升級 msw 之後要重跑**，否則版本對不上 worker 會拒絕啟動：
 *   pnpm --filter @greygray/api-client exec msw init ../../apps/admin/public
 */

let started: Promise<unknown> | null = null;

function startOnce() {
  if (started) return started;
  started = (async () => {
    const [{ isMockEnabled }, { adminWorker }] = await Promise.all([
      import('@greygray/api-client/mock/env'),
      import('@greygray/api-client/mock/browser'),
    ]);
    if (!isMockEnabled()) return;
    await adminWorker.start({
      // 沒有 handler 的請求（Next.js 的 HMR、字型、圖片）一律放行，不要吵。
      onUnhandledRequest: 'bypass',
      quiet: false,
    });
    // eslint-disable-next-line no-console
    console.info('[mock] admin worker 已啟動（NEXT_PUBLIC_USE_MOCK=1）');
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
