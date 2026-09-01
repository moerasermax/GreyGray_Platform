/**
 * 首頁顯示的數字必須來自 API——「現在卡在哪」#29 的迴歸測試。
 *
 * ── 為什麼這個檔案是 FE-21 最重要的產出 ──
 * #29（首頁寫死 NT$1,280,000／NT$860,000 與 6 筆假分錄，同一時間帳務頁與 API
 * 是 NT$0／-NT$1,920 與 1 筆）能一路活到上線，**是因為沒有任何測試斷言過
 * 「首頁顯示的數字來自 API」**。修掉 fixture 只解決今天；沒有這個檔案，
 * 下一個人為了「先看看版面」再塞一次假資料時，全套測試一樣會全綠。
 *
 * ── 為什麼是 `renderToStaticMarkup` 而不是 testing-library ──
 * 這個 workspace 沒有 jsdom、沒有 @testing-library，而 FE-21 的
 * allow 清單不含 `package.json`，不能加相依。`react-dom/server` 是既有相依，
 * 在 node 環境下就能把元件畫成 HTML 字串。代價是 **effect 不會跑**，
 * 所以拆成兩段各自可驗的事：
 *   1. 取數層（`_lib/dashboardLedger.ts`）用 msw 打真的 `ApiClient`，
 *      斷言「拿到的就是端點回的」與「失敗時沒有任何 data」
 *   2. 畫面層用 `renderToStaticMarkup` 餵入①的結果，斷言「畫出來的就是①拿到的」
 * 再加上第三段：整頁的**首次 render**（還沒有任何 API 回應）一個金額都不准出現——
 * 那正是 #29 當時的狀態，這一條在修好之前是紅的（見 `.dispatch/reports/FE-21.md`）。
 *
 * JSX 在這裡走 classic transform（tsconfig 是 `jsx: preserve`，vitest 的 esbuild
 * 因此產 `React.createElement`），而 `page.tsx` 自己的 JSX 也一樣——
 * 它模組作用域裡沒有 `React`，所以要把 `React` 掛到 global 上。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { ApiClient, formatMoney } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { ToastProvider } from '@greygray/ui/admin';
import { LiabilityVsCashSection } from '../_components/LiabilityVsCashCard';
import * as dashboardMock from '../_lib/dashboardMock';
import {
  LOADING,
  RECENT_ENTRIES_LIMIT,
  buildRecentEntriesQuery,
  flattenEntries,
  loadLiabilityVsCash,
  loadRecentLedgerEntries,
  loadRelatedEntries,
  sortLedgerLines,
  type JournalEntry,
} from '../_lib/dashboardLedger';

(globalThis as unknown as { React: typeof React }).React = React;

type S = components['schemas'];

const BASE_URL = 'http://localhost:5001';

/** 2026-09-01 Leader 用 Owner 身分實打 `GET /v1/ledger/liability-vs-cash` 的回傳。 */
const LIVE_LIABILITY: S['LiabilityVsCash'] = {
  customerLiabilityTotal: { amountMinor: 0, currency: 'TWD' },
  cashTotal: { amountMinor: -192000, currency: 'TWD' },
  asOf: '2026-09-01T12:43:01Z',
  isBreached: true,
};

const ENTRY: JournalEntry = {
  id: '0199f0c2-0000-7000-8000-00000000ab01',
  occurredAt: '2026-09-01T11:00:00Z',
  postedAt: '2026-09-01T11:00:02Z',
  sourceModule: 'Procurement',
  sourceRef: 'TRIP-2026Q3-JP01',
  memo: '進貨',
  lines: [
    { accountCode: '1310', accountName: '存貨', direction: 'Debit', amount: { amountMinor: 192000, currency: 'TWD' } },
    { accountCode: '1010', accountName: '銀行存款', direction: 'Credit', amount: { amountMinor: 192000, currency: 'TWD' } },
  ],
};

const SECOND_ENTRY: JournalEntry = {
  id: '0199f0c2-0000-7000-8000-00000000ab02',
  occurredAt: '2026-08-31T02:00:00Z',
  postedAt: '2026-08-31T02:00:01Z',
  sourceModule: 'Procurement',
  sourceRef: 'TRIP-2026Q3-JP01',
  memo: '進貨退回',
  lines: [
    { accountCode: '1010', accountName: '銀行存款', direction: 'Debit', amount: { amountMinor: 5000, currency: 'TWD' } },
    { accountCode: '1310', accountName: '存貨', direction: 'Credit', amount: { amountMinor: 5000, currency: 'TWD' } },
  ],
};

/** W3C traceparent。`ApiError.shortTraceId` 取的是**整串的後 8 碼**，不是 span id。 */
const TRACE_ID = '00-aaaaaaaabbbbbbbbccccccccdddddddd-eeeeeeee-01';

/** 每次請求的完整 URL，用來斷言篩選條件真的送給了端點而不是在前端過濾。 */
let seenUrls: string[] = [];

const handlers = [
  http.get(`${BASE_URL}/v1/ledger/liability-vs-cash`, ({ request }) => {
    seenUrls.push(request.url);
    return HttpResponse.json(LIVE_LIABILITY);
  }),
  http.get(`${BASE_URL}/v1/ledger/entries`, ({ request }) => {
    seenUrls.push(request.url);
    const sourceRef = new URL(request.url).searchParams.get('sourceRef');
    const items = sourceRef === ENTRY.sourceRef ? [ENTRY, SECOND_ENTRY] : [ENTRY];
    return HttpResponse.json({ items, nextCursor: null });
  }),
];

function failWith(path: string, code: string, title: string) {
  return http.get(`${BASE_URL}${path}`, () =>
    HttpResponse.json(
      { type: 'about:blank', title, status: 503, code, traceId: TRACE_ID },
      { status: 503, headers: { 'content-type': 'application/problem+json' } },
    ),
  );
}

const server = setupServer(...handlers);
const client = new ApiClient({ baseUrl: BASE_URL });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
beforeEach(() => {
  seenUrls = [];
});
afterEach(() => server.resetHandlers(...handlers));
afterAll(() => server.close());

/** 畫面上所有的金額字串。`formatMoney` 對 TWD 一律輸出 `NT$` 前綴（ADR-028）。 */
function moneyStringsIn(html: string): string[] {
  return html.match(/-?NT\$[\d,.]+/g) ?? [];
}

function render(node: React.ReactElement): string {
  return renderToStaticMarkup(node);
}

describe('#29 首頁的財務數字必須來自 API', () => {
  it('負債 vs 現金：畫出來的兩個金額逐字等於端點回傳的那一筆', async () => {
    const state = await loadLiabilityVsCash(client);

    expect(state).toEqual({ status: 'ready', data: LIVE_LIABILITY });
    expect(seenUrls).toEqual([`${BASE_URL}/v1/ledger/liability-vs-cash`]);

    const html = render(<LiabilityVsCashSection state={state} onRetry={() => {}} />);

    // 畫面上出現的金額**只有**端點回的那兩個，沒有第三個來路不明的數字。
    expect(moneyStringsIn(html)).toEqual([
      formatMoney(LIVE_LIABILITY.customerLiabilityTotal),
      formatMoney(LIVE_LIABILITY.cashTotal),
    ]);

    // 逐字釘住 2026-09-01 當下的真實值，避免哪天 formatMoney 跟著壞掉也沒人發現。
    expect(moneyStringsIn(html)).toEqual(['NT$0', '-NT$1,920']);
  });

  it('負債 vs 現金：端點失敗時一個金額都不畫，改顯示錯誤與追蹤碼', async () => {
    server.use(failWith('/v1/ledger/liability-vs-cash', 'ledger.unavailable', '帳務服務暫時無法使用。'));

    const state = await loadLiabilityVsCash(client);

    expect(state.status).toBe('error');
    // 失敗狀態在型別上就沒有 data，不可能 fallback 回寫死的數字。
    expect(state).not.toHaveProperty('data');

    const html = render(<LiabilityVsCashSection state={state} onRetry={() => {}} />);

    expect(moneyStringsIn(html)).toEqual([]);
    expect(html).toContain('帳務服務暫時無法使用。');
    expect(html).toContain(TRACE_ID.slice(-8));
  });

  it('負債 vs 現金：載入中一個金額都不畫', () => {
    const html = render(<LiabilityVsCashSection state={LOADING} onRetry={() => {}} />);

    expect(moneyStringsIn(html)).toEqual([]);
    expect(html).toContain('正在讀取負債 vs 現金');
  });

  it('★ 整頁首次 render（還沒有任何 API 回應）不准出現任何金額', async () => {
    const { default: DashboardPage } = await import('../page');

    const html = render(
      <ToastProvider>
        <DashboardPage />
      </ToastProvider>,
    );

    // 修好之前這裡會拿到 ['NT$1,280,000','NT$860,000','NT$4,200', …] 共 13 個。
    expect(moneyStringsIn(html)).toEqual([]);
    // KPI 四塊維持誠實的空值，不是猜出來的數字。
    expect(html.match(/尚未提供/g)).toHaveLength(dashboardMock.DASHBOARD_KPIS.length);
  });
});

describe('#29 首頁的最近分錄必須來自 API', () => {
  it('分錄列表：拿到的就是端點回的，攤平後每一行的金額都等於端點回的金額', async () => {
    const state = await loadRecentLedgerEntries(client, { sourceModule: '', from: null, to: null });

    expect(state).toEqual({ status: 'ready', data: [ENTRY] });

    const rows = state.status === 'ready' ? flattenEntries(state.data) : [];
    expect(rows.map((row) => formatMoney(row.line.amount, { showDecimals: true }))).toEqual(
      ENTRY.lines.map((line) => formatMoney(line.amount, { showDecimals: true })),
    );
  });

  it('分錄列表：端點失敗時回錯誤狀態，沒有任何 data 可以拿來畫', async () => {
    server.use(failWith('/v1/ledger/entries', 'ledger.unavailable', '分錄暫時查不到。'));

    const state = await loadRecentLedgerEntries(client, { sourceModule: '', from: null, to: null });

    expect(state.status).toBe('error');
    expect(state).not.toHaveProperty('data');
  });

  it('篩選條件送給端點，不是在前端記憶體裡過濾', async () => {
    await loadRecentLedgerEntries(client, { sourceModule: '  Ordering  ', from: '2026-08-01', to: '2026-08-31' });

    const url = new URL(seenUrls[0] ?? '');
    expect(url.pathname).toBe('/v1/ledger/entries');
    expect(url.searchParams.get('sourceModule')).toBe('Ordering');
    expect(url.searchParams.get('from')).toBe('2026-08-01');
    expect(url.searchParams.get('to')).toBe('2026-08-31');
    expect(url.searchParams.get('limit')).toBe(String(RECENT_ENTRIES_LIMIT));
  });

  it('沒填的篩選條件不送空參數', () => {
    expect(buildRecentEntriesQuery({ sourceModule: '   ', from: null, to: null })).toEqual({
      limit: RECENT_ENTRIES_LIMIT,
    });
  });

  it('Drawer 的「相關分錄」用 sourceRef 打端點，帶回同一張單的其他分錄', async () => {
    const state = await loadRelatedEntries(client, ENTRY.sourceRef);

    expect(new URL(seenUrls[0] ?? '').searchParams.get('sourceRef')).toBe(ENTRY.sourceRef);
    expect(state).toEqual({ status: 'ready', data: [ENTRY, SECOND_ENTRY] });

    // 兩筆分錄、各兩行，攤平成四行——只用 selectedEntry.lines 會少掉退回那一筆。
    expect(state.status === 'ready' ? flattenEntries(state.data) : []).toHaveLength(4);
  });

  it('排序只重排端點回的這一頁，不改任何金額', () => {
    const rows = flattenEntries([ENTRY, SECOND_ENTRY]);
    const asc = sortLedgerLines(rows, 'amount', 'asc');

    expect(asc.map((row) => row.line.amount.amountMinor)).toEqual([5000, 5000, 192000, 192000]);
    expect(sortLedgerLines(rows, 'postedAt', 'desc')[0]?.entry.id).toBe(ENTRY.id);
    // 原陣列不被就地改動，元素也是原本那些物件（沒有被複製或改寫過的金額）。
    expect(rows.map((row) => row.line.amount.amountMinor)).toEqual([192000, 192000, 5000, 5000]);
    expect(asc.every((row) => rows.includes(row))).toBe(true);
  });
});

describe('#29 fixture 不准再出現在程式碼裡', () => {
  it('_lib/dashboardMock.ts 只剩 KPI，沒有任何 FIXTURE 匯出', () => {
    expect(Object.keys(dashboardMock)).toEqual(['DASHBOARD_KPIS']);
    expect(dashboardMock.DASHBOARD_KPIS.every((kpi) => kpi.label && kpi.hint)).toBe(true);
  });
});
