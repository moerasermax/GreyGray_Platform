/**
 * `@greygray/ui` —— 兩個 app 共用的設計層。
 *
 * **token 是唯一的顏色與尺寸來源。** 元件裡不准出現 raw hex、不准出現硬寫的 px 圓角。
 * 平行開發時這條特別要緊：四個人各自挑一個顏色，出來就是四個產品。
 *
 * - `tokens/soft-seoul.css` —— 前台（ADR-009）。全站沒有一個直角。
 * - `tokens/admin.css` —— 後台。中性、密、有深色模式、金額欄位 tabular-nums。
 *
 * CSS 檔在各 app 的 `globals.css` 用 `@import` 引入，不從這個 barrel 匯出。
 *
 * 元件由前端工作包逐一補上；這裡先只放跨 app 共用的型別與工具。
 */

export type ThemeName = 'soft-seoul' | 'admin';

/** 對比度都實際量過（WCAG 2.1）。細節與用途分工見 `tokens/soft-seoul.css` 的檔頭。 */
export const SoftSeoulPalette = {
  /** 淺奶茶，只給漸層與裝飾。上面不可以放淺色字。 */
  decor: 'var(--gg-decor)',
  /** 按鈕、標籤、選中態底色。上面放深字（--gg-on-primary）7.10:1 ✓ */
  primary: 'var(--gg-primary)',
  /** 淺底上的強調文字與連結，7.77:1 ✓ */
  primaryText: 'var(--gg-primary-text)',
  /** 焦點框、選中框線。非文字元件 ≥ 3:1 ✓ */
  primaryStrong: 'var(--gg-primary-strong)',
} as const;

// ── FE-2：Soft Seoul 元件庫 ──────────────────────────────────────────────
// 就 docs/06-前端工作包.md FE-2 那張表列出的元件，不多做。

export * from './components/icons';

export * from './components/Button';
export * from './components/IconButton';
export * from './components/Spinner';
export * from './components/Card';
export * from './components/ProductCard';
export * from './components/CategoryChip';
export * from './components/Badge';
export * from './components/FavoriteHeart';
export * from './components/SearchBar';
export * from './components/Avatar';
export * from './components/PriceDisplay';
export * from './components/QuantityStepper';
export * from './components/BottomActionBar';
export * from './components/TopBar';
export * from './components/BottomSheet';
export * from './components/Dialog';
export * from './components/Toast';
export * from './components/Skeleton';
export * from './components/EmptyState';
export * from './components/ErrorState';
export * from './components/Countdown';
export * from './components/Field';
export * from './components/Input';
export * from './components/PasswordInput';
export * from './components/Select';
export * from './components/Textarea';
export * from './components/Tabs';
export * from './components/Thumbnail';
