/**
 * 儀表板的暫時假資料。
 *
 * FE-1 的 mock server（MSW）跟 `src/endpoints/admin.ts` 還沒做出來，
 * 這裡先用固定 fixture 頂著，**不要放進 `packages/api-client`**（那是 FE-1 的地盤）。
 * 真正串接時要換成：
 *   - `GET /v1/ledger/liability-vs-cash` → {@link LiabilityVsCash}
 *   - `GET /v1/ledger/entries`（`sourceModule` / `from` / `to` 篩選）→ 分錄列表
 *
 * KPI 那四塊（使用中開團／待處理訂單／待採購／今日已出貨）**契約裡沒有對應的彙總端點**。
 * 曾經用假數字撐版面，2026-08-30 拔掉了——營運會相信後台上的數字，
 * 佔位值放在正式環境比沒有更危險。現在顯示「尚未提供」，不猜也不用列表 API 在前端加總
 * （列表有分頁，加出來的只是這一頁的合計，而且違反鐵則 2）。等後端補彙總端點再串。
 */
import type { Money } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';

export type LiabilityVsCash = components['schemas']['LiabilityVsCash'];
export type Direction = components['schemas']['Direction'];

export interface LedgerEntryRow {
  readonly id: string;
  readonly postedAt: string;
  readonly sourceModule: string;
  readonly sourceRef: string;
  readonly memo: string;
  readonly accountName: string;
  readonly direction: Direction;
  readonly amount: Money;
}

function twd(major: number): Money {
  return { amountMinor: Math.round(major * 100), currency: 'TWD' };
}

/** isBreached = true：現在就是負債 > 現金的狀態，首頁要顯眼地擋在最上面。 */
export const LIABILITY_VS_CASH_FIXTURE: LiabilityVsCash = {
  customerLiabilityTotal: twd(1_280_000),
  cashTotal: twd(860_000),
  isBreached: true,
  asOf: '2026-08-28T01:15:00Z',
};

export interface DashboardKpi {
  readonly key: string;
  readonly label: string;
  readonly hint: string;
}

export const DASHBOARD_KPIS: readonly DashboardKpi[] = [
  { key: 'open-campaigns', label: '使用中的開團', hint: 'Open ＋ TripInProgress，等後端提供彙總端點' },
  { key: 'pending-orders', label: '待處理訂單', hint: 'AwaitingPayment ＋ PaidAwaitingClose，等後端提供彙總端點' },
  { key: 'pending-purchase', label: '待採購項目', hint: '尚未標記已購/缺貨，等後端提供彙總端點' },
  { key: 'shipped-today', label: '今日已出貨', hint: '過去 24 小時內 dispatch，等後端提供彙總端點' },
];

export const LEDGER_ENTRIES_FIXTURE: readonly LedgerEntryRow[] = [
  {
    id: 'je-0142-1',
    postedAt: '2026-08-27T13:02:00+08:00',
    sourceModule: 'Ordering',
    sourceRef: 'ORD-20260827-0142',
    memo: '訂單付款',
    accountName: '銀行存款',
    direction: 'Debit',
    amount: twd(4200),
  },
  {
    id: 'je-0142-2',
    postedAt: '2026-08-27T13:02:00+08:00',
    sourceModule: 'Ordering',
    sourceRef: 'ORD-20260827-0142',
    memo: '訂單付款',
    accountName: '預收貨款',
    direction: 'Credit',
    amount: twd(4200),
  },
  {
    id: 'je-0139-1',
    postedAt: '2026-08-27T11:40:00+08:00',
    sourceModule: 'Ordering',
    sourceRef: 'ORD-20260827-0139',
    memo: '訂單付款（含運）',
    accountName: '綠界在途',
    direction: 'Debit',
    amount: twd(2860),
  },
  {
    id: 'je-0139-2',
    postedAt: '2026-08-27T11:40:00+08:00',
    sourceModule: 'Ordering',
    sourceRef: 'ORD-20260827-0139',
    memo: '訂單付款（含運）',
    accountName: '預收貨款',
    direction: 'Credit',
    amount: twd(2600),
  },
  {
    id: 'je-0139-3',
    postedAt: '2026-08-27T11:40:00+08:00',
    sourceModule: 'Ordering',
    sourceRef: 'ORD-20260827-0139',
    memo: '訂單付款（含運）',
    accountName: '預收運費',
    direction: 'Credit',
    amount: twd(260),
  },
  {
    id: 'je-trip01-1',
    postedAt: '2026-08-26T20:15:00+08:00',
    sourceModule: 'Procurement',
    sourceRef: 'TRIP-2026Q3-JP01',
    memo: '日本採購旅程成本入帳',
    accountName: '旅程成本',
    direction: 'Debit',
    amount: twd(18_000),
  },
  {
    id: 'je-trip01-2',
    postedAt: '2026-08-26T20:15:00+08:00',
    sourceModule: 'Procurement',
    sourceRef: 'TRIP-2026Q3-JP01',
    memo: '日本採購旅程成本入帳',
    accountName: '銀行存款',
    direction: 'Credit',
    amount: twd(18_000),
  },
  {
    id: 'je-shp0087-1',
    postedAt: '2026-08-26T16:30:00+08:00',
    sourceModule: 'Fulfillment',
    sourceRef: 'SHP-20260826-0087',
    memo: '出貨結轉銷貨成本',
    accountName: '銷貨成本',
    direction: 'Debit',
    amount: twd(3100),
  },
  {
    id: 'je-shp0087-2',
    postedAt: '2026-08-26T16:30:00+08:00',
    sourceModule: 'Fulfillment',
    sourceRef: 'SHP-20260826-0087',
    memo: '出貨結轉銷貨成本',
    accountName: '存貨',
    direction: 'Credit',
    amount: twd(3100),
  },
  {
    id: 'je-refund0021-1',
    postedAt: '2026-08-25T09:05:00+08:00',
    sourceModule: 'Ledger',
    sourceRef: 'REFUND-20260825-0021',
    memo: '缺貨退款（退成儲值金，零手續費）',
    accountName: '客戶儲值金',
    direction: 'Debit',
    amount: twd(650),
  },
  {
    id: 'je-refund0021-2',
    postedAt: '2026-08-25T09:05:00+08:00',
    sourceModule: 'Ledger',
    sourceRef: 'REFUND-20260825-0021',
    memo: '缺貨退款（退成儲值金，零手續費）',
    accountName: '預收貨款',
    direction: 'Credit',
    amount: twd(650),
  },
];
