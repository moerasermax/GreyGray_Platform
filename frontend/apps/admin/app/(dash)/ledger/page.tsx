'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import {
  getCampaignMargin,
  getLiabilityVsCash,
  listCampaigns,
  listLedgerEntries,
  type ListLedgerEntriesQuery,
} from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import {
  DataTable,
  DateRangePicker,
  DirectionCell,
  Drawer,
  ErrorState,
  Field,
  FilterBar,
  Input,
  LockIcon,
  MoneyCell,
  type DataTableColumn,
  type DateRange,
} from '@greygray/ui/admin';
import { useEffect, useMemo, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { LiabilityVsCashCard } from '../_components/LiabilityVsCashCard';
import { CampaignMarginPanel } from './_components/CampaignMarginPanel';

type S = components['schemas'];
type JournalEntry = S['JournalEntry'];

interface EntryLineRow {
  readonly key: string;
  readonly entry: JournalEntry;
  readonly line: JournalEntry['lines'][number];
}

function flatten(entries: readonly JournalEntry[]): EntryLineRow[] {
  return entries.flatMap((entry) =>
    entry.lines.map((line, index) => ({ key: `${entry.id}:${index}`, entry, line })),
  );
}

export default function LedgerPage() {
  const [liability, setLiability] = useState<S['LiabilityVsCash'] | null>(null);
  const [liabilityError, setLiabilityError] = useState<Error | null>(null);

  const [campaigns, setCampaigns] = useState<readonly S['AdminCampaign'][]>([]);
  const [marginCampaignId, setMarginCampaignId] = useState('');
  const [margin, setMargin] = useState<S['CampaignMargin'] | null>(null);
  const [marginLoading, setMarginLoading] = useState(false);

  const [sourceModule, setSourceModule] = useState('');
  const [sourceRef, setSourceRef] = useState('');
  const [dateRange, setDateRange] = useState<DateRange>({ from: null, to: null });
  const [entries, setEntries] = useState<readonly JournalEntry[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [entriesLoading, setEntriesLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [entriesError, setEntriesError] = useState<ApiError | Error | null>(null);
  const [selectedEntry, setSelectedEntry] = useState<JournalEntry | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    void getLiabilityVsCash(browserApi())
      .then((data) => {
        if (!cancelled) setLiability(data);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setLiabilityError(cause instanceof Error ? cause : new Error('讀取負債 vs 現金失敗。'));
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  useEffect(() => {
    let cancelled = false;
    void listCampaigns(browserApi(), { limit: 100 }).then(
      (page) => {
        if (!cancelled) setCampaigns(page.items);
      },
      () => {
        // 選單用，拿不到就留空。
      },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (!marginCampaignId) {
      setMargin(null);
      return;
    }
    let cancelled = false;
    setMarginLoading(true);
    void getCampaignMargin(browserApi(), marginCampaignId)
      .then((data) => {
        if (!cancelled) setMargin(data);
      })
      .catch(() => {
        if (!cancelled) setMargin(null);
      })
      .finally(() => {
        if (!cancelled) setMarginLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [marginCampaignId]);

  function buildEntriesQuery(cursor?: string): ListLedgerEntriesQuery {
    return {
      ...(sourceModule.trim() ? { sourceModule: sourceModule.trim() } : {}),
      ...(sourceRef.trim() ? { sourceRef: sourceRef.trim() } : {}),
      ...(dateRange.from ? { from: dateRange.from } : {}),
      ...(dateRange.to ? { to: dateRange.to } : {}),
      ...(cursor ? { cursor } : {}),
      limit: 20,
    };
  }

  useEffect(() => {
    let cancelled = false;
    setEntriesLoading(true);
    setEntriesError(null);
    void listLedgerEntries(browserApi(), buildEntriesQuery())
      .then((page) => {
        if (cancelled) return;
        setEntries(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setEntriesError(cause instanceof Error ? cause : new Error('讀取分錄失敗。'));
      })
      .finally(() => {
        if (!cancelled) setEntriesLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sourceModule, sourceRef, dateRange, reloadKey]);

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listLedgerEntries(browserApi(), buildEntriesQuery(nextCursor));
      setEntries((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (cause) {
      setEntriesError(cause instanceof Error ? cause : new Error('讀取下一頁失敗。'));
    } finally {
      setLoadingMore(false);
    }
  }

  const rows = useMemo(() => flatten(entries), [entries]);

  const columns: DataTableColumn<EntryLineRow>[] = [
    {
      key: 'postedAt',
      header: '過帳時間',
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
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={formatMoney(row.line.amount, { showDecimals: true })} />,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold text-fg">帳務</h1>

      {liabilityError ? (
        <ErrorState title={liabilityError.message} onRetry={() => setReloadKey((current) => current + 1)} />
      ) : liability ? (
        <div className="flex flex-col gap-2">
          <LiabilityVsCashCard data={liability} />
          <p className="text-xs text-fg-muted">
            客戶負債＝預收貨款＋預收運費＋客戶儲值金，三者加總已經由後端算好；
            若 <span className="font-medium text-danger">正在用還沒交貨的錢過日子</span>，
            往下用「分錄查詢」以 <span className="gg-numeric">sourceModule = Ledger</span> 篩選，
            可以看到手動調整或退款相關的分錄軌跡。
          </p>
        </div>
      ) : (
        <div className="h-24 animate-pulse rounded-card bg-surface-sunken" />
      )}

      <CampaignMarginPanel
        campaigns={campaigns}
        campaignId={marginCampaignId}
        onSelectCampaign={setMarginCampaignId}
        margin={margin}
        loading={marginLoading}
      />

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-sm font-semibold text-fg-muted">分錄查詢</h2>
          <div className="flex items-center gap-1.5 rounded-full bg-surface-sunken px-3 py-1 text-xs text-fg-muted">
            <LockIcon className="h-3.5 w-3.5" />
            <span>分錄一經過帳即不可修改或刪除，更正只能開立反向分錄</span>
          </div>
        </div>

        <FilterBar>
          <Field label="來源模組" htmlFor="ledger-filter-source-module" hint="例如 Ordering、Procurement">
            <Input
              id="ledger-filter-source-module"
              value={sourceModule}
              onChange={(event) => setSourceModule(event.target.value)}
            />
          </Field>
          <Field label="來源單號" htmlFor="ledger-filter-source-ref" hint="訂單編號或旅程代碼">
            <Input
              id="ledger-filter-source-ref"
              value={sourceRef}
              onChange={(event) => setSourceRef(event.target.value)}
            />
          </Field>
          <DateRangePicker idPrefix="ledger-filter-posted" label="過帳日期" value={dateRange} onChange={setDateRange} />
        </FilterBar>

        {entriesError ? (
          <ErrorState
            title={entriesError instanceof ApiError ? entriesError.problem.title : entriesError.message}
            traceId={entriesError instanceof ApiError ? entriesError.shortTraceId : null}
            onRetry={() => setReloadKey((current) => current + 1)}
          />
        ) : (
          <>
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(row) => row.key}
              loading={entriesLoading}
              onRowClick={(row) => setSelectedEntry(row.entry)}
              emptyTitle="這段條件下沒有分錄"
              emptyDescription="換個日期範圍或清空篩選看看。"
            />
            {nextCursor ? (
              <div className="flex justify-center">
                <button
                  type="button"
                  onClick={() => void handleLoadMore()}
                  disabled={loadingMore}
                  className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
                >
                  {loadingMore ? '載入中…' : '載入更多'}
                </button>
              </div>
            ) : null}
          </>
        )}
      </section>

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
            <div className="flex items-start gap-2 rounded-card bg-surface-sunken p-3 text-xs text-fg-muted">
              <LockIcon className="mt-0.5 h-4 w-4 shrink-0" />
              <p>這裡沒有編輯也沒有刪除。分錄一經過帳即不可修改，更正只能另開一筆反向分錄沖銷。</p>
            </div>
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
                {selectedEntry.lines.map((line, index) => (
                  // eslint-disable-next-line react/no-array-index-key
                  <tr key={index} className="border-b border-border-soft last:border-0">
                    <td className="py-2">
                      <span className="gg-numeric mr-1 text-xs text-fg-muted">{line.accountCode}</span>
                      {line.accountName}
                    </td>
                    <DirectionCell direction={line.direction} className="py-2" />
                    <MoneyCell value={formatMoney(line.amount, { showDecimals: true })} className="py-2" />
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </Drawer>
    </div>
  );
}
