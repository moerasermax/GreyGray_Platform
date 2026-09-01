/**
 * 前台底部分頁列的**判斷邏輯**：這一頁該不該有分頁列、現在停在哪一個分頁、
 * 以及分頁列自己佔多高。抽成純函式住在這裡，是因為這個 workspace 沒有 jsdom
 * 也沒有 `@testing-library`（而且不打算為了測試加相依），
 * 渲染層測不到的東西，只有先抽出來才測得到。
 *
 * ── 為什麼是「預設顯示 ＋ 例外隱藏」而不是反過來 ──
 * 「現在卡在哪」#30 的病就是**頁面沒有出口**。用白名單（只有列出來的頁面才有分頁列）
 * 的話，下一個新頁面預設會**沒有導覽**——那正是 #30 本身，只是換一頁再發作一次。
 * 用黑名單則相反：新頁面自動有出口，代價是「新頁面若用了 `BottomActionBar`
 * 會疊兩條固定列」。那個代價有 `__tests__/bottomActionBarCollision.test.ts` 機械擋著
 * （它去原始碼裡真的找誰用了 `BottomActionBar`），所以選黑名單。
 */

/** 分頁列高度。詳見 `TAB_BAR_HEIGHT` 的說明，那不是隨手挑的數字。 */
export const TAB_BAR_HEIGHT =
  'calc(var(--gg-bottom-bar-height) + env(safe-area-inset-bottom, 0px))';

/** 分頁列自己的下方內距，讓 iPhone 的 home indicator 不會壓在分頁上。 */
export const TAB_BAR_SAFE_AREA_PADDING = 'env(safe-area-inset-bottom, 0px)';

// ── 該不該顯示 ────────────────────────────────────────────────────────────

export interface TabBarRule {
  /** 路由樣式。`:xxx` 是動態片段，比對時吃任何一段。 */
  readonly route: string;
  /** 命中這條規則時，分頁列要不要出現。 */
  readonly visible: boolean;
  /** 為什麼。這一欄不是註解，是規則的一部分——沒有理由的例外會被下一個人刪掉。 */
  readonly reason: string;
}

/**
 * **第一條命中的規則決定結果；一條都沒命中就顯示。**
 *
 * 順序有意義：`/payment/result` 必須排在 `/payment/:orderId` 前面，
 * 否則 `result` 會被當成 orderId 一起吃掉。
 */
export const TAB_BAR_RULES: readonly TabBarRule[] = [
  {
    route: '/payment/result',
    visible: true,
    reason: '付款結果頁沒有固定底部列，而且是流程終點，正需要出口',
  },
  {
    route: '/payment/:orderId',
    visible: false,
    reason: '這一頁會自動 POST 導轉綠界，中途讓人切走等於把付款丟在半路',
  },
  {
    route: '/products/:productId',
    visible: false,
    reason: 'AddToCartPanel 已經有 BottomActionBar（fixed bottom-0、72px），會疊在一起',
  },
  {
    route: '/cart',
    visible: false,
    reason: 'BottomActionBar：前往結帳',
  },
  {
    route: '/checkout',
    visible: false,
    reason: 'BottomActionBar：送出訂單',
  },
];

/**
 * 正規化 `usePathname()` 或手寫進來的路徑：去掉查詢字串、hash 與結尾的斜線。
 * 空字串與 `'/'` 都視為首頁。
 */
export function normalizePathname(pathname: string): string {
  const beforeHash = pathname.split('#')[0] ?? '';
  const beforeQuery = beforeHash.split('?')[0] ?? '';
  const trimmed = beforeQuery.replace(/\/+$/, '');
  return trimmed === '' ? '/' : trimmed;
}

function segmentsOf(pathname: string): string[] {
  return pathname.split('/').filter((segment) => segment.length > 0);
}

/** 逐段比對，段數要一樣。`:xxx` 吃任何一段。 */
function matchesRoute(route: string, pathname: string): boolean {
  const routeSegments = segmentsOf(route);
  const pathSegments = segmentsOf(pathname);
  if (routeSegments.length !== pathSegments.length) return false;
  return routeSegments.every(
    (segment, index) => segment.startsWith(':') || segment === pathSegments[index],
  );
}

/** 命中的規則；沒有規則命中回 `null`（＝走預設的「顯示」）。 */
export function tabBarRuleFor(pathname: string): TabBarRule | null {
  const path = normalizePathname(pathname);
  return TAB_BAR_RULES.find((rule) => matchesRoute(rule.route, path)) ?? null;
}

/** 這一頁該不該畫分頁列。 */
export function shouldShowTabBar(pathname: string): boolean {
  return tabBarRuleFor(pathname)?.visible ?? true;
}

// ── 四個分頁 ──────────────────────────────────────────────────────────────

export type TabIconName = 'home' | 'campaign' | 'cart' | 'account';

export interface StorefrontTab {
  readonly href: string;
  readonly label: string;
  readonly icon: TabIconName;
  /** 除了 `href` 本身，還有哪些路徑算「人在這一個分頁底下」。前綴比對，逐段。 */
  readonly alsoActiveFor: readonly string[];
}

/**
 * 首頁 · 開團 · 購物車 · 我的（使用者 2026-09-02 拍板的四個）。
 *
 * 「我的」指到 `/orders` 而不是某個帳號首頁，是因為**前台沒有帳號首頁**——
 * `/orders`、`/wallet`、`/addresses` 三頁各自獨立，訂單是其中最常回訪的一頁。
 */
export const STOREFRONT_TABS: readonly StorefrontTab[] = [
  { href: '/', label: '首頁', icon: 'home', alsoActiveFor: ['/products', '/categories'] },
  { href: '/campaigns', label: '開團', icon: 'campaign', alsoActiveFor: [] },
  { href: '/cart', label: '購物車', icon: 'cart', alsoActiveFor: ['/checkout'] },
  {
    href: '/orders',
    label: '我的',
    icon: 'account',
    // 登入／註冊算在「我的」底下：未登入的人點「我的」會被 router.replace 丟到 /login。
    alsoActiveFor: ['/wallet', '/addresses', '/login', '/register'],
  },
];

/** `path` 是不是落在 `prefix` 這一段底下。逐段比對，`/campaignsfoo` 不算在 `/campaigns` 底下。 */
function isUnderPrefix(path: string, prefix: string): boolean {
  return path === prefix || path.startsWith(`${prefix}/`);
}

/**
 * 現在停在哪一個分頁（回傳該分頁的 `href`）；都不是就回 `null`。
 *
 * `'/'` 只吃精確比對——它是所有路徑的前綴，拿它做前綴比對會讓每一頁都亮「首頁」。
 */
export function activeTabHref(pathname: string): string | null {
  const path = normalizePathname(pathname);

  const exact = STOREFRONT_TABS.find((tab) => tab.href === path);
  if (exact) return exact.href;

  const byPrefix = STOREFRONT_TABS.find((tab) =>
    [tab.href, ...tab.alsoActiveFor].some(
      (prefix) => prefix !== '/' && isUnderPrefix(path, prefix),
    ),
  );
  return byPrefix?.href ?? null;
}
