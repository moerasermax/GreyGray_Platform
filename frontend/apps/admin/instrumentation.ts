/*
 * SSR 端的 mock 掛載點。
 *
 * server component 裡的 `fetch` **不會**經過瀏覽器的 Service Worker，
 * 所以瀏覽器端那組 worker 完全攔不到它。Next.js 的 `register()` 在 server 啟動時
 * 跑一次，是唯一能在任何 server component 執行前把 `setupServer` 掛上的地方。
 *
 * 後端好了之後只要把 `NEXT_PUBLIC_USE_MOCK` 拿掉，這裡整段就是 no-op，
 * 頁面程式碼一行都不用改（這是 FE-1 那一包的設計目標）。
 */
export async function register() {
  if (process.env.NEXT_RUNTIME !== 'nodejs') return;
  if (process.env['NEXT_PUBLIC_USE_MOCK'] !== '1') return;

  const { adminServer } = await import('./app/_mock/server.node');
  adminServer.listen({ onUnhandledRequest: 'bypass' });
  // eslint-disable-next-line no-console
  console.info('[mock] admin SSR server 已啟動（NEXT_PUBLIC_USE_MOCK=1）');
}
