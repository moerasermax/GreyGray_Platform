import type { NextConfig } from 'next';

const config: NextConfig = {
  reactStrictMode: true,

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
