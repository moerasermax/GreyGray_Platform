import type { NextConfig } from 'next';

const config: NextConfig = {
  reactStrictMode: true,

  /*
   * msw/node 內部 import `@mswjs/interceptors/ClientRequest` 這個子路徑匯出，
   * 被 webpack 打包時解析不到，dev 會直接 500（build 反而看不出來）。
   * 列進 serverExternalPackages 讓 Next 交給原生 Node 解析。
   * 這兩個套件只在 NEXT_PUBLIC_USE_MOCK=1 時才會被 instrumentation.ts 動態載入。
   */
  serverExternalPackages: ['msw', '@mswjs/interceptors'],

  /*
   * dev 一定要跑 --turbopack（package.json 的 dev script 已經帶了）。
   * webpack 的 dev 編譯會用瀏覽器條件去解析 instrumentation.ts 這條路徑上的
   * `@mswjs/interceptors`，而它的 `./ClientRequest` 在 browser 條件下是 null，
   * 整站直接 500——而 `next build` 反而是過的，所以只跑 build 看不出來。
   */

  // workspace 內的套件是 TypeScript 原始碼，要讓 Next 自己編譯。
  transpilePackages: ['@greygray/ui', '@greygray/api-client'],

  // 商品圖放 Cloudflare R2。白名單要明確列出，不要用萬用字元。
  images: {
    remotePatterns: [
      { protocol: 'https', hostname: 'cdn.greygray.tw' },
    ],
  },

  // 正式機是 native process + NSSM（不用 Docker，ADR-003），
  // standalone 產出可以整包搬過去。
  output: 'standalone',

  poweredByHeader: false,

  /*
   * Next.js 內建設定，預設 false（見 node_modules/next/dist/server/config-shared.d.ts
   * 的 ExperimentalConfig，這個欄位不在官方型別裡，是刻意用型別斷言繞過型別檢查啟用的）。
   * ★ 2026-08-31 FE-18 即時驗證：這個 flag 對正式機的部署拓樸沒有效果，見
   *   .dispatch/reports/FE-18.md「我發現但沒做的事」——`next-server.js` 的
   *   `attachRequestMeta()` 在 `this.fetchHostname && this.port` 皆真時
   *   （standalone `server.js` 只要吃到 `HOSTNAME` 環境變數就會是真，
   *   正式機 `ops/deploy.ps1` 固定設 `HOSTNAME=127.0.0.1`）會直接短路，
   *   永遠用 `HOSTNAME:PORT` 組 origin，根本不會讀到這個欄位。
   *   而且 standalone build 產出的 `required-server-files.json`
   *   在非 Vercel 環境下會被 Next 的 build 流程強制覆寫回 false
   *   （`next/dist/build/index.js` 的 `trustHostHeader: _ciinfo.hasNextSupport`），
   *   這裡設的 `true` 連寫進產物都寫不進去。留著這行只是保留診斷軌跡，
   *   不代表症狀已解——見自驗報告，真正的修法還沒定案。
   */
  experimental: {
    trustHostHeader: true,
  } as NonNullable<NextConfig['experimental']>,
};

export default config;
