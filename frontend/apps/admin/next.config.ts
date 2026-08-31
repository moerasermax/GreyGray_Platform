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
};

export default config;
