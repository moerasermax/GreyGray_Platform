import type { Metadata, Viewport } from 'next';
import { Nunito, Noto_Sans_TC } from 'next/font/google';
import './globals.css';

/*
 * 字體用 next/font 自架，不要用 <link> 拉 Google Fonts——
 * 外部連結會有一次額外的 DNS + TLS，而且字體換手時整頁會跳（CLS）。
 *
 * Nunito 沒有繁體中文字符，所以中文會自動落到 Noto Sans TC：
 * 拉丁字母與數字圓潤（貼近韓系柔美的調性），中文正常。
 * 順序寫反的話中文能顯示，但數字會變成 Noto 的直筆畫，整個調性就散了。
 */
const nunito = Nunito({
  subsets: ['latin'],
  weight: ['400', '600', '700', '800'],
  variable: '--font-nunito',
  display: 'swap',
});

const notoSansTC = Noto_Sans_TC({
  subsets: ['latin'],
  weight: ['400', '500', '700'],
  variable: '--font-noto-tc',
  display: 'swap',
});

export const metadata: Metadata = {
  title: {
    default: 'GreyGray',
    template: '%s｜GreyGray',
  },
  description: '出國採購開團與本地現貨，一起買更划算。',
};

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  // 不要設 maximumScale 或 userScalable: false——那會擋掉視力不佳的人放大。
  themeColor: '#fdf2f8',
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="zh-Hant-TW" className={`${nunito.variable} ${notoSansTC.variable}`}>
      <body>{children}</body>
    </html>
  );
}
