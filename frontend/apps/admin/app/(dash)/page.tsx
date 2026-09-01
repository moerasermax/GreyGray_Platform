'use client';

import { formatMoney } from '@greygray/api-client';
import {
  DataTable,
  DateRangePicker,
  Drawer,
  DirectionCell,
  ErrorState,
  Field,
  FilterBar,
  Input,
  KpiTile,
  LockIcon,
  MoneyCell,
  type DataTableColumn,
  type DateRange,
  type SortDirection,
} from '@greygray/ui/admin';
import { useEffect, useMemo, useState } from 'react';
import { browserApi } from '../_lib/apiClient';
import { LiabilityVsCashSection } from './_components/LiabilityVsCashCard';
import { DASHBOARD_KPIS } from './_lib/dashboardMock';
import {
  LOADING,
  RECENT_ENTRIES_LIMIT,
  flattenEntries,
  isLedgerSortKey,
  loadLiabilityVsCash,
  loadRecentLedgerEntries,
  loadRelatedEntries,
  sortLedgerLines,
  type JournalEntry,
  type LedgerLineRow,
  type LedgerSortKey,
  type LiabilityVsCash,
  type LoadState,
} from './_lib/dashboardLedger';

/**
 * 後台首頁。
 *
 * ★ 這一頁上的每一個金額都來自 `GET /v1/ledger/liability-vs-cash` 與
 *   `GET /v1/ledger/entries`，形狀照抄同一個 app 的 `(dash)/ledger/page.tsx`。
 *   2026-09-01 之前這裡是寫死的 fixture，跟帳務頁差了 128 萬（「現在卡在哪」#29）。
 *   **載入中與失敗時不顯示任何數字**——退回「先擺著的值」正是那個 bug 的成因。
 *
 * 四個 KPI 卡維持「尚未提供」：契約裡沒有彙總端點，而列表 API 有分頁，
 * 在前端加總只會得到「這一頁的合計」（理由見 `_lib/dashboardMock.ts`）。
 */
export default function DashboardPage() {
  const [sourceModule, setSourceModule] = useState('');
  const [dateRange, setDateRange] = useState<DateRange>({ from: null, to: null });
  const [sortKey, setSortKey] = useState<LedgerSortKey>('postedAt');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const [reloadKey, setReloadKey] = useState(0);

  const [liability, setLiability] = useState<LoadState<LiabilityVsCash>>(LOADING);
  const [entries, setEntries] = useState<LoadState<readonly JournalEntry[]>>(LOADING);

  const [selectedEntry, setSelectedEntry] = useState<JournalEntry | null>(null);
  const [related, setRelated] = useState<LoadState<readonly JournalEntry[]>>(LOADING);

  useEffect(() => {
    let cancelled = false;
    setLiability(LOADING);
    void loadLiabilityVsCash(browserApi()).then((state) => {
      if (!cancelled) setLiability(state);
    });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  useEffect(() => {
    let cancelled = false;
    setEntries(LOADING);
    void loadRecentLedgerEntries(browserApi(), {
      sourceModule,
      from: dateRange.from,
      to: dateRange.to,
    }).then((state) => {
      if (!cancelled) setEntries(state);
    });
    return () => {
      cancelled = true;
    };
  }, [sourceModule, dateRange, reloadKey]);

  // 「相關分錄」：同一張單可能不只一筆分錄（付款、退款、沖銷），用 sourceRef 反查端點。
  useEffect(() => {
    const sourceRef = selectedEntry?.sourceRef;
    setRelated(LOADING);
    if (!sourceRef) return;

    let cancelled = false;
    void loadRelatedEntries(browserApi(), sourceRef).then((state) => {
      if (!cancelled) setRelated(state);
    });
    return () => {
      cancelled = true;
    };
  }, [selectedEntry, reloadKey]);

  const rows = useMemo(() => {
    if (entries.status !== 'ready') return [];
    return sortLedgerLines(flattenEntries(entries.data), sortKey, sortDirection);
  }, [entries, sortKey, sortDirection]);

  const relatedRows = useMemo(
    () => (related.status === 'ready' ? flattenEntries(related.data) : []),
    [related],
  );

  const busy = liability.status === 'loading' || entries.status === 'loading';

  function handleSortChange(key: string) {
    if (!isLedgerSortKey(key)) return;
    if (key === sortKey) {
      setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDirection('desc');
    }
  }

  function reload() {
    setReloadKey((current) => current + 1);
  }

  const columns: DataTableColumn<LedgerLineRow>[] = [
    {
      key: 'postedAt',
      header: '過帳時間',
      sortable: true,
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(
            new Date(row.entry.postedAt),
          )}
        </td>
      ),
    },
    {
      key: 'source',
      header: '來源',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <div className="font-medium text-fg">{row.entry.sourceRef}</div>
          <div className="text-xs text-fg-muted">{row.entry.sourceModule}</div>
        </td>
      ),
    },
    {
      key: 'accountName',
      header: '科目',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg">
          <span className="gg-numeric mr-1 text-xs text-fg-muted">{row.line.accountCode}</span>
          {row.line.accountName}
        </td>
      ),
    },
    {
      key: 'direction',
      header: '方向',
      renderCell: (row) => <DirectionCell direction={row.line.direction} />,
    },
    {
      key: 'amount',
      header: '金額',
      sortable: true,
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={formatMoney(row.line.amount, { showDecimals: true })} />,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-fg">儀表板</h1>
        <button
          type="button"
          onClick={reload}
          disabled={busy}
          className="rounded-full border border-border-strong px-3 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
        >
          {busy ? '載入中…' : '重新整理'}
        </button>
      </div>

      <LiabilityVsCashSection state={liability} onRetry={reload} />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {DASHBOARD_KPIS.map((kpi) => (
          <KpiTile key={kpi.key} label={kpi.label} value="尚未提供" hint={kpi.hint} />
        ))}
      </div>

      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-sm font-semibold text-fg-muted">最近分錄</h2>
          <p className="text-xs text-fg-muted">
            最多顯示端點回傳的前 {RECENT_ENTRIES_LIMIT} 筆，完整查詢與分頁在「帳務」頁
          </p>
        </div>
        <FilterBar>
          <Field label="來源模組" htmlFor="filter-source-module">
            <Input
              id="filter-source-module"
              placeholder="例如 Ordering"
              value={sourceModule}
              onChange={(event) => setSourceModule(event.target.value)}
            />
          </Field>
          <DateRangePicker idPrefix="filter-posted" label="過帳日期" value={dateRange} onChange={setDateRange} />
        </FilterBar>

        {entries.status === 'error' ? (
          <ErrorState title={entries.title} traceId={entries.traceId} onRetry={reload} />
        ) : (
          <DataTable
            columns={columns}
            rows={rows}
            getRowKey={(row) => row.key}
            loading={entries.status === 'loading'}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onSortChange={handleSortChange}
            onRowClick={(row) => setSelectedEntry(row.entry)}
            emptyTitle="這段期間沒有分錄"
            emptyDescription="換個日期範圍或清空來源模組篩選看看。"
          />
        )}
      </div>

      <Drawer
        open={selectedEntry !== null}
        onClose={() => setSelectedEntry(null)}
        title={selectedEntry ? `分錄：${selectedEntry.sourceRef}` : ''}
      >
        {selectedEntry ? (
          <div className="flex flex-col gap-4">
            {selectedEntry.memo ? (
              <div>
                <p className="text-xs text-fg-muted">摘要</p>
                <p className="text-sm text-fg">{selectedEntry.memo}</p>
              </div>
            ) : null}
            <div className="flex items-start gap-2 rounded-card bg-surface-sunken p-3 text-xs text-fg-on-tint">
              <LockIcon className="mt-0.5 h-4 w-4 shrink-0" />
              <p>這裡沒有編輯也沒有刪除。分錄一經過帳即不可修改，更正只能另開一筆反向分錄沖銷。</p>
            </div>

            {related.status === 'loading' ? (
              <div
                className="h-24 animate-pulse rounded-card bg-surface-sunken"
                role="status"
                aria-label="正在讀取相關分錄"
              />
            ) : related.status === 'error' ? (
              <ErrorState title={related.title} traceId={related.traceId} onRetry={reload} />
            ) : (
              <table className="w-full border-collapse text-sm">
                <thead>
                  <tr className="border-b border-border-soft text-left text-fg-muted">
                    <th className="py-2 text-xs font-medium">科目</th>
                    <th className="py-2 text-xs font-medium">方向</th>
                    <th className="py-2 text-right text-xs font-medium" data-numeric>
                      金額
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {relatedRows.map((row) => (
                    <tr key={row.key} className="border-b border-border-soft last:border-0">
                      <td className="py-2">
                        <span className="gg-numeric mr-1 text-xs text-fg-muted">{row.line.accountCode}</span>
                        {row.line.accountName}
                      </td>
                      <DirectionCell direction={row.line.direction} className="py-2" />
                      <MoneyCell value={formatMoney(row.line.amount, { showDecimals: true })} className="py-2" />
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        ) : null}
      </Drawer>
    </div>
  );
}
