/**
 * 客服小幫手不擋住任何頁面的主要操作（派工書 §1.1「⚠ 版面規則」§3.5）。
 *
 * ── 為什麼是掃原始碼，不是截圖 ──
 * 這個 workspace 沒有 jsdom，量不出真的 layout；而 dev server 由 Leader 管，
 * 這一包不跑 `next build`／不重開 dev server（`.dispatch/PROMPTS.md` 慣例）。
 * 改用可以機械驗證的東西：收合按鈕與 `BottomActionBar`／`StorefrontTabBar`
 * 共用**同一個 CSS token** `--gg-bottom-bar-height`，只要三邊都繼續讀同一個 token，
 * 按鈕就一定貼在那條固定列的正上方、不會疊在一起——不必逐頁量座標。
 * 真瀏覽器 360px 走查仍待補，寫進 `.dispatch/reports/FE-35.md`「我發現但沒做的事」。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const SUPPORT_WIDGET_SRC = readFileSync(join(APP_ROOT, '_components', 'SupportWidget.tsx'), 'utf8');
const LAYOUT_SRC = readFileSync(join(APP_ROOT, 'layout.tsx'), 'utf8');
const CHECKOUT_SRC = readFileSync(join(APP_ROOT, '(checkout)', 'checkout', 'page.tsx'), 'utf8');
const CART_SRC = readFileSync(join(APP_ROOT, '(checkout)', 'cart', 'page.tsx'), 'utf8');
const BOTTOM_ACTION_BAR_SRC = readFileSync(
  join(APP_ROOT, '..', '..', '..', 'packages', 'ui', 'src', 'components', 'BottomActionBar.tsx'),
  'utf8',
);

describe('SupportWidget 掛在全站的殼上', () => {
  it('layout.tsx 有 import 並渲染 <SupportWidget />', () => {
    expect(LAYOUT_SRC).toMatch(/import\s*\{\s*SupportWidget\s*\}\s*from\s*'\.\/_components\/SupportWidget'/);
    expect(LAYOUT_SRC).toMatch(/<SupportWidget\s*\/>/);
  });

  it('渲染在 <body> 底下（跟 StorefrontTabBar 同一層），不是包在某個 route group 裡才有', () => {
    const bodyOpen = LAYOUT_SRC.indexOf('<body>');
    const widgetUse = LAYOUT_SRC.indexOf('<SupportWidget');
    expect(bodyOpen).toBeGreaterThan(-1);
    expect(widgetUse).toBeGreaterThan(bodyOpen);
  });
});

describe('收合按鈕不疊在底部固定列上', () => {
  it('bottom 算式使用跟 BottomActionBar 相同的 token（--gg-bottom-bar-height），保證貼在它上方', () => {
    expect(SUPPORT_WIDGET_SRC).toContain('var(--gg-bottom-bar-height)');
    expect(BOTTOM_ACTION_BAR_SRC).toContain('var(--gg-bottom-bar-height)');
  });

  it('bottom 算式也留了 safe-area-inset-bottom（360px／有瀏海手機都算進去）', () => {
    expect(SUPPORT_WIDGET_SRC).toContain('env(safe-area-inset-bottom, 0px)');
  });

  it('不是寫死一個 px 數字——寫死的話下次 token 改高度這裡不會跟著變', () => {
    expect(SUPPORT_WIDGET_SRC).not.toMatch(/bottom:\s*['"`]?\d+px/);
  });
});

describe('結帳頁與購物車頁的主要按鈕仍然在（不是被小幫手取代或蓋住）', () => {
  it('結帳頁還有「送出訂單」，包在 BottomActionBar 裡', () => {
    expect(CHECKOUT_SRC).toContain('BottomActionBar');
    expect(CHECKOUT_SRC).toContain('送出訂單');
  });

  it('購物車頁還有「前往結帳」，包在 BottomActionBar 裡', () => {
    expect(CART_SRC).toContain('BottomActionBar');
    expect(CART_SRC).toContain('前往結帳');
  });
});

describe('關閉鈕：BottomSheet 本身的關閉入口沒有被繞過', () => {
  it('SupportWidget 把 onClose 接到狀態機的 close，而不是自己重畫一個沒有 aria-label 的關閉鈕', () => {
    expect(SUPPORT_WIDGET_SRC).toMatch(/<BottomSheet[\s\S]*?onClose=\{.*close.*\}/);
  });
});
