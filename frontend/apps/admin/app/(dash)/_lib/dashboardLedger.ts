/**
 * 首頁「負債 vs 現金」與「最近分錄」的取數層。
 *
 * ── 為什麼不是直接寫在 `page.tsx` 的 `useEffect` 裡 ──
 * #29（首頁顯示寫死的 NT$1,280,000／NT$860,000，同一時間 API 回的是
 * NT$0／-NT$1,920）能一路活到上線，是因為**沒有任何測試斷言過
 * 「首頁顯示的數字來自 API」**。這個環境沒有 jsdom 也沒有 testing-library，
 * 元件的 effect 跑不起來；把「打哪支端點、拿到什麼、失敗了怎麼辦」
 * 抽成純函式，才有辦法用 msw 直接釘住（見 `../__tests__/dashboardLedger.test.tsx`）。
 *
 * ★ 三支載入函式刻意都不 throw，也刻意**沒有**「拿不到就用預設值」那條路。
 *   `LoadState` 只有三態，`ready` 以外的狀態在型別上就拿不到 `data`——
 *   fallback 回寫死的數字正是 #29 的成因，所以那條路不該存在，而不是「記得別走」。
 *
 * 端點與型別都是既有的（`packages/api-client`），這裡不新增也不修改契約。
 */
import { ApiError } from '@greygray/api-client';
import type { ApiClient } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import {
  getLiabilityVsCash,
  listLedgerEntries,
  type ListLedgerEntriesQuery,
} from '@greygray/api-client/endpoints/admin';

type S = components['schemas'];

export type LiabilityVsCash = S['LiabilityVsCash'];
export type JournalEntry = S['JournalEntry'];

/** 錯誤狀態刻意不帶任何 `data`——見檔頭。 */
export interface LoadErrorState {
  readonly status: 'error';
  /** `ApiError` 就用 `problem.title`，其餘用訊息，都不是給機器判斷用的。 */
  readonly title: string;
  /** `ApiError.shortTraceId`，讓使用者可以把後 8 碼報給工程。 */
  readonly traceId: string | null;
}

export type LoadState<T> =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly data: T }
  | LoadErrorState;

export const LOADING = { status: 'loading' } as const;

/** 首頁只放最近幾筆；完整查詢與分頁在帳務頁（`(dash)/ledger`）。 */
export const RECENT_ENTRIES_LIMIT = 20;

/** Drawer 用 `sourceRef` 反查同一張單的分錄，一張單不該有幾十筆。 */
export const RELATED_ENTRIES_LIMIT = 50;

export function toErrorState(cause: unknown, fallbackTitle: string): LoadErrorState {
  if (cause instanceof ApiError) {
    return { status: 'error', title: cause.problem.title, traceId: cause.shortTraceId };
  }
  return {
    status: 'error',
    title: cause instanceof Error && cause.message ? cause.message : fallbackTitle,
    traceId: null,
  };
}

export async function loadLiabilityVsCash(client: ApiClient): Promise<LoadState<LiabilityVsCash>> {
  try {
    return { status: 'ready', data: await getLiabilityVsCash(client) };
  } catch (cause) {
    return toErrorState(cause, '讀取負債 vs 現金失敗。');
  }
}

export interface RecentEntriesFilters {
  readonly sourceModule: string;
  readonly from: string | null;
  readonly to: string | null;
}

/**
 * 篩選條件送給端點，不在前端記憶體裡過濾。
 *
 * 前端過濾只濾得到「這一頁」，看起來像是「這段期間沒有分錄」，
 * 其實是下一頁還沒載。空字串一律不送，避免打出 `?sourceModule=` 這種空參數。
 */
export function buildRecentEntriesQuery(filters: RecentEntriesFilters): ListLedgerEntriesQuery {
  const sourceModule = filters.sourceModule.trim();
  return {
    ...(sourceModule ? { sourceModule } : {}),
    ...(filters.from ? { from: filters.from } : {}),
    ...(filters.to ? { to: filters.to } : {}),
    limit: RECENT_ENTRIES_LIMIT,
  };
}

export async function loadRecentLedgerEntries(
  client: ApiClient,
  filters: RecentEntriesFilters,
): Promise<LoadState<readonly JournalEntry[]>> {
  try {
    const page = await listLedgerEntries(client, buildRecentEntriesQuery(filters));
    return { status: 'ready', data: page.items };
  } catch (cause) {
    return toErrorState(cause, '讀取分錄失敗。');
  }
}

/** Drawer 的「相關分錄」：同一張單可能有不只一筆分錄（付款、退款、沖銷）。 */
export async function loadRelatedEntries(
  client: ApiClient,
  sourceRef: string,
): Promise<LoadState<readonly JournalEntry[]>> {
  try {
    const page = await listLedgerEntries(client, { sourceRef, limit: RELATED_ENTRIES_LIMIT });
    return { status: 'ready', data: page.items };
  } catch (cause) {
    return toErrorState(cause, '讀取相關分錄失敗。');
  }
}

/** 一筆分錄有多個借貸行，表格一行顯示一個借貸行——與帳務頁同一套攤平方式。 */
export interface LedgerLineRow {
  readonly key: string;
  readonly entry: JournalEntry;
  readonly line: JournalEntry['lines'][number];
}

export function flattenEntries(entries: readonly JournalEntry[]): LedgerLineRow[] {
  return entries.flatMap((entry) =>
    entry.lines.map((line, index) => ({ key: `${entry.id}:${index}`, entry, line })),
  );
}

export type LedgerSortKey = 'postedAt' | 'amount';

export function isLedgerSortKey(key: string): key is LedgerSortKey {
  return key === 'postedAt' || key === 'amount';
}

/**
 * 只排序端點回傳的這一頁，不是全部——首頁本來就只放最近幾筆。
 *
 * 比 `amountMinor` 是排序用的比較，不是金額運算（鐵則 2）：
 * 結果只決定列的先後，沒有任何被比較出來的數字會顯示在畫面上。
 */
export function sortLedgerLines(
  rows: readonly LedgerLineRow[],
  key: LedgerSortKey,
  direction: 'asc' | 'desc',
): LedgerLineRow[] {
  const sign = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => {
    if (key === 'amount') return (a.line.amount.amountMinor - b.line.amount.amountMinor) * sign;
    return a.entry.postedAt.localeCompare(b.entry.postedAt) * sign;
  });
}
