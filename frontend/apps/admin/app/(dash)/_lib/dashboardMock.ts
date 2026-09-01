/**
 * 儀表板首頁上「契約裡沒有端點可以問」的那幾塊。
 *
 * ── 2026-09-01（FE-21）之後，這個檔案裡沒有任何假資料 ──
 * 原本那兩個寫死的 fixture（負債 vs 現金、分錄列表）已整段刪除
 * ——名字刻意不寫在這裡，派工書的驗收條件之一就是全 repo grep 不到它們。
 * 首頁的財務數字改打 `GET /v1/ledger/liability-vs-cash`、分錄改打
 * `GET /v1/ledger/entries`，載入邏輯在 `./dashboardLedger.ts`。
 * 那兩個 fixture 讓首頁顯示 NT$1,280,000／NT$860,000 並跳紅字警示，
 * 而同一時間 API 與帳務頁是 NT$0／-NT$1,920——兩頁互相矛盾，
 * 而老闆是看首頁做經營判斷的（「現在卡在哪」#29）。
 *
 * 檔名沒有跟著改，是刻意的：`docs/24-前端第十波派工書.md` §0／§3 逐字引用
 * 這個路徑，而 `.dispatch/audit-dispatch.sh` 第 ④ 項會確認派工書引用的檔案存在，
 * 改名會讓那一項變成 ✗。要改名請連同派工書一起改。
 *
 * KPI 那四塊（使用中開團／待處理訂單／待採購／今日已出貨）**契約裡沒有對應的彙總端點**。
 * 曾經用假數字撐版面，2026-08-30 拔掉了——營運會相信後台上的數字，
 * 佔位值放在正式環境比沒有更危險。現在顯示「尚未提供」，不猜也不用列表 API 在前端加總
 * （列表有分頁，加出來的只是這一頁的合計，而且違反鐵則 2）。等後端補彙總端點再串。
 */

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
