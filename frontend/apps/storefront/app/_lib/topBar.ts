/**
 * 前台**頂部列**的判斷邏輯：這一頁該不該有頂部列、返回要去哪。
 * 跟 `tabs.ts` 一樣抽成純函式住在這裡，理由也一樣——這個 workspace 沒有 jsdom
 * 也沒有 `@testing-library`，渲染層測不到的東西只有先抽出來才測得到。
 *
 * ── 為什麼要有頂部列 ──
 * 「現在卡在哪」#32：商品詳情、購物車、結帳三頁**畫面上沒有任何出口**，
 * 使用者第二次撞到同一件事。這三頁不放分頁列的判斷本身是對的
 * （它們已經有 `BottomActionBar`，兩條固定列會疊在一起），
 * 錯在只寫了「不要顯示」卻沒有給替代出口。手機電商的商品頁確實不放分頁列，
 * 但它們一定有一條頂部列。
 *
 * ── 為什麼分頁列用黑名單、頂部列卻用白名單 ──
 * 看起來不對稱，其實是同一個保證的兩半。分頁列預設**顯示**，所以新頁面天生就有出口
 * （`tabs.ts` 檔頭那段：白名單會讓下一個新頁面預設沒有導覽＝#30 換一頁再發作）。
 * 新頁面既然已經有分頁列，就不該再多一條頂部列，所以頂部列預設**不顯示**。
 * 真正的保證不在任何一份清單裡，而在
 * `__tests__/pageShell.test.ts`：**每一條路由恰好有一種殼**。
 * 有人把某頁的分頁列關掉卻忘了給頂部列，那支測試會紅，
 * 而它的路由清單是去原始碼掃 `page.tsx` 掃出來的，不是抄的。
 */

// ── 該不該顯示、返回去哪 ──────────────────────────────────────────────────

export interface TopBarRule {
  /** 路由樣式。`:xxx` 是動態片段，比對時吃任何一段。與 `tabs.ts` 同一套語法。 */
  readonly route: string;
  /**
   * 沒有站內上一頁時，返回要去哪。**一定要有值**：使用者可能是直接打網址、
   * 從外部連結、或從掃 QR code 進來的，那時 `history.back()` 會把人丟出站外
   * （甚至什麼都不做）。「返回」按了沒反應是這個專案一再踩到的形狀。
   */
  readonly backHref: string;
  /** 右邊要不要放帶徽章的購物車連結。 */
  readonly showCartLink: boolean;
  /** 為什麼。跟 `TAB_BAR_RULES` 的 `reason` 同理——沒有理由的規則會被下一個人刪掉。 */
  readonly reason: string;
}

/**
 * **有頂部列的頁面就這三頁**（使用者 2026-09-02 拍板：只有這三頁，不做全站頂部頁首）。
 *
 * 順序不影響結果（三條路由互不重疊），但維持與 `TAB_BAR_RULES` 相同的排列，
 * 兩份清單對照著看比較不會漏。
 */
export const TOP_BAR_RULES: readonly TopBarRule[] = [
  {
    route: '/products/:productId',
    backHref: '/',
    showCartLink: true,
    reason:
      '分頁列被 BottomActionBar 擠掉了，這頁只剩「加入購物車」。' +
      '加完之後要看得到車也回得去，所以右邊放帶徽章的購物車',
  },
  {
    route: '/cart',
    backHref: '/',
    showCartLink: false,
    reason: '同樣被 BottomActionBar 擠掉分頁列；人已經在購物車了，右邊再放一顆購物車沒有意義',
  },
  {
    route: '/checkout',
    backHref: '/cart',
    showCartLink: false,
    reason:
      '同上。返回回到 /cart 而不是 /——結帳的上一步就是購物車，' +
      '把人直接丟回首頁等於要他從頭再找一次',
  },
];

export interface ShellExceptionRule {
  readonly route: string;
  /** `true` = 兩種殼都不放。`false` 這一條是**擋在前面的否決**，見下面的順序說明。 */
  readonly exempt: boolean;
  readonly reason: string;
}

/**
 * **兩種殼都不放的頁面。**
 * 這份清單是 `__tests__/pageShell.test.ts` 的唯一豁免出口，
 * 每一條都要寫得出理由，加一條進來就等於要說服下一個讀的人。
 *
 * ★ **順序有意義，理由與 `TAB_BAR_RULES` 完全相同**：`:orderId` 吃任何一段，
 * 所以 `/payment/result` 會被 `/payment/:orderId` 一起吃掉。第一版就是這樣寫的，
 * `topBar.test.ts` 那條「`/payment/result` 不是例外」當場紅——
 * 而那一頁是付款流程的終點，是最需要出口的一頁。
 * 這裡不用「先寫先贏」以外的機制（例如按靜態片段數排序），
 * 是因為 `tabs.ts` 已經是這個形狀，兩份規則表用同一套讀法比較不會有人讀錯。
 */
export const SHELL_EXCEPTIONS: readonly ShellExceptionRule[] = [
  {
    route: '/payment/result',
    exempt: false,
    reason:
      '付款結果頁是流程終點，正需要出口（分頁列給它）。' +
      '必須排在 /payment/:orderId 前面，否則會被當成一個 orderId 吃掉',
  },
  {
    route: '/payment/:orderId',
    exempt: true,
    reason:
      '這一頁載入後自動 POST 導轉綠界，停留不到一秒。' +
      '放任何出口都是邀請使用者把付款丟在半路——這也是 TAB_BAR_RULES 藏分頁列的同一個理由',
  },
];

function segmentsOf(pathname: string): string[] {
  return pathname.split('/').filter((segment) => segment.length > 0);
}

/**
 * 逐段比對，段數要一樣。`:xxx` 吃任何一段。
 * 與 `tabs.ts` 的同名函式邏輯相同；沒有把它 export 出來共用，是因為那會讓
 * 兩份規則表在「怎麼比對路由」上綁在一起，而**兩支測試各自釘住自己那一份**才擋得住漂移。
 */
function matchesRoute(route: string, pathname: string): boolean {
  const routeSegments = segmentsOf(route);
  const pathSegments = segmentsOf(pathname);
  if (routeSegments.length !== pathSegments.length) return false;
  return routeSegments.every(
    (segment, index) => segment.startsWith(':') || segment === pathSegments[index],
  );
}

/** 正規化路徑：去掉查詢字串、hash 與結尾斜線。空字串與 `'/'` 都是首頁。 */
export function normalizePathname(pathname: string): string {
  const beforeHash = pathname.split('#')[0] ?? '';
  const beforeQuery = beforeHash.split('?')[0] ?? '';
  const trimmed = beforeQuery.replace(/\/+$/, '');
  return trimmed === '' ? '/' : trimmed;
}

/** 命中的規則；沒有命中回 `null`（＝這一頁沒有頂部列）。 */
export function topBarRuleFor(pathname: string): TopBarRule | null {
  const path = normalizePathname(pathname);
  return TOP_BAR_RULES.find((rule) => matchesRoute(rule.route, path)) ?? null;
}

/** 這一頁該不該畫頂部列。**預設不畫**，理由見檔頭。 */
export function shouldShowTopBar(pathname: string): boolean {
  return topBarRuleFor(pathname) !== null;
}

/** 這一頁是不是「兩種殼都不放」的例外。**第一條命中的規則決定結果**，一條都沒命中就不是。 */
export function isShellException(pathname: string): boolean {
  const path = normalizePathname(pathname);
  return SHELL_EXCEPTIONS.find((exception) => matchesRoute(exception.route, path))?.exempt ?? false;
}

/**
 * 返回的後備目標。沒有頂部列的頁面回 `null`。
 *
 * **這是「沒有上一頁時去哪」，不是「上一頁是什麼」。**
 * 直接打網址進來、從 LINE 分享點進來、掃 QR code 進來的人都會走到這條。
 */
export function backHrefFor(pathname: string): string | null {
  return topBarRuleFor(pathname)?.backHref ?? null;
}

/** 這一頁的頂部列右邊要不要放購物車連結。 */
export function shouldShowCartLink(pathname: string): boolean {
  return topBarRuleFor(pathname)?.showCartLink ?? false;
}

// ── 返回要不要走 history.back() ───────────────────────────────────────────

export interface HistoryBackInput {
  /** `document.referrer`。同分頁的站內導覽會有值；直接打網址、新分頁開啟則是空字串。 */
  readonly referrer: string;
  /** `window.location.origin`。 */
  readonly origin: string;
  /** `window.history.length`。1 代表這個分頁只有現在這一頁。 */
  readonly historyLength: number;
}

/**
 * 返回鍵要不要攔下來走 `history.back()`。
 *
 * **`false` 時走 `<a href>` 的預設導覽**，所以任何一種判斷失準的後果都只是
 * 「回到後備目標」而不是「按了沒反應」——這個方向是刻意選的。
 *
 * 三個條件都要成立：
 * - `referrer` 有值（`''` 代表新分頁、直接打網址、或從外部隱藏 referrer 的來源進來）
 * - referrer 與現在同一個 origin（跨站的上一頁不算站內，回去等於把人丟出站外）
 * - `history.length > 1`（新分頁第一頁的 `back()` 什麼事都不會發生）
 *
 * ⚠ 用 `referrer` 而不是自己記一份導覽堆疊，是因為 App Router 的 client 端導覽
 * **不會更新 `document.referrer`**——所以這個判斷偏保守：
 * 站內用 `<Link>` 逛好幾頁之後，referrer 仍停在最初那一次整頁載入。
 * 偏保守的那一側是「走後備目標」，不會壞掉，只是少一次「回到上一頁」。
 */
export function shouldUseHistoryBack({
  referrer,
  origin,
  historyLength,
}: HistoryBackInput): boolean {
  if (referrer === '' || origin === '') return false;
  if (historyLength <= 1) return false;

  let referrerOrigin: string;
  try {
    referrerOrigin = new URL(referrer).origin;
  } catch {
    // 壞掉的 referrer 不猜，走後備目標。
    return false;
  }
  return referrerOrigin === origin;
}
