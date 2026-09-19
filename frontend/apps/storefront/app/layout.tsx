import type { Metadata, Viewport } from 'next';
import { Nunito, Noto_Sans_TC } from 'next/font/google';
import './globals.css';
import { MockBootstrap } from './_mock/MockBootstrap';
import { StorefrontTabBar } from './_components/StorefrontTabBar';
import { SupportWidget } from './_components/SupportWidget';

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
      <body>
        <MockBootstrap />
        {children}
        {/*
          底部分頁列（#30）。掛在 `{children}` 之後、`<body>` 直屬層，
          理由是它是**全站的殼**，不屬於任何一個 route group——
          前台的 (shop)／(checkout)／(account) 沒有共同的中介 layout，
          這裡是唯一「一處解決全部」的位置。哪幾頁不畫由 `_lib/tabs.ts` 決定。

          ★ 這裡刻意**不再補一層底部留白**。`globals.css` 給 `<body>` 的
          `padding-bottom: calc(var(--gg-bottom-bar-height) + env(safe-area-inset-bottom, 0px))`
          已經是全站每一頁都有的留白，而分頁列的高度就寫成同一個算式
          （`_lib/tabs.ts` 的 `TAB_BAR_HEIGHT`），恰好把它填滿。
          在這裡再加一次會變成 144px，多出整整一條列的空白。
          兩邊逐字相同由 `_lib/__tests__/tabBarReservesBottomSpace.test.ts` 釘住。
        */}
        <StorefrontTabBar />

        {/*
          右下角客服小幫手（ADR-040，FE-35）。跟 `StorefrontTabBar` 一樣掛在全站的殼，
          理由相同：前台沒有共同的中介 layout。它自己算好 `bottom` 貼在分頁列／
          `BottomActionBar` 之上，不需要跟著這裡的留白算式改。
        */}
        <SupportWidget />
      </body>
    </html>
  );
}
