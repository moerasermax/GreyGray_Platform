'use client';

import { formatMoney } from '@greygray/api-client';
import {
  DataTable,
  DateRangePicker,
  Drawer,
  DirectionCell,
  Field,
  FilterBar,
  Input,
  KpiTile,
  MoneyCell,
  useToast,
  type DataTableColumn,
  type DateRange,
  type SortDirection,
} from '@greygray/ui/admin';
import { useMemo, useState } from 'react';
import { LiabilityVsCashCard } from './_components/LiabilityVsCashCard';
import {
  DASHBOARD_KPIS,
  LEDGER_ENTRIES_FIXTURE,
  LIABILITY_VS_CASH_FIXTURE,
  type LedgerEntryRow,
} from './_lib/dashboardMock';

type SortKey = 'postedAt' | 'amount';

export default function DashboardPage() {
  const toast = useToast();
  const [sourceModule, setSourceModule] = useState('');
  const [dateRange, setDateRange] = useState<DateRange>({ from: null, to: null });
  const [sortKey, setSortKey] = useState<SortKey>('postedAt');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');
  const [selectedEntry, setSelectedEntry] = useState<LedgerEntryRow | null>(null);

  const filteredRows = useMemo(() => {
    const keyword = sourceModule.trim().toLowerCase();
    const filtered = LEDGER_ENTRIES_FIXTURE.filter((row) => {
      if (keyword && !row.sourceModule.toLowerCase().includes(keyword)) return false;
      const posted = row.postedAt.slice(0, 10);
      if (dateRange.from && posted < dateRange.from) return false;
      if (dateRange.to && posted > dateRange.to) return false;
      return true;
    });

    const sorted = [...filtered].sort((a, b) => {
      const direction = sortDirection === 'asc' ? 1 : -1;
      if (sortKey === 'amount') return (a.amount.amountMinor - b.amount.amountMinor) * direction;
      return a.postedAt.localeCompare(b.postedAt) * direction;
    });
    return sorted;
  }, [sourceModule, dateRange, sortKey, sortDirection]);

  const relatedLines = selectedEntry
    ? LEDGER_ENTRIES_FIXTURE.filter((row) => row.sourceRef === selectedEntry.sourceRef)
    : [];

  function handleSortChange(key: string) {
    if (key !== 'postedAt' && key !== 'amount') return;
    if (key === sortKey) {
      setSortDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDirection('desc');
    }
  }

  const columns: DataTableColumn<LedgerEntryRow>[] = [
    {
      key: 'postedAt',
      header: '過帳時間',
      sortable: true,
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(
            new Date(row.postedAt),
          )}
        </td>
      ),
    },
    {
      key: 'source',
      header: '來源',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <div className="font-medium text-fg">{row.sourceRef}</div>
          <div className="text-xs text-fg-muted">{row.sourceModule}</div>
        </td>
      ),
    },
    {
      key: 'accountName',
      header: '科目',
      renderCell: (row) => <td className="px-3 py-2 text-fg">{row.accountName}</td>,
    },
    {
      key: 'direction',
      header: '方向',
      renderCell: (row) => <DirectionCell direction={row.direction} />,
    },
    {
      key: 'amount',
      header: '金額',
      sortable: true,
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={formatMoney(row.amount, { showDecimals: true })} />,
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-fg">儀表板</h1>
        <button
          type="button"
          onClick={() => toast.show('success', '已重新整理資料')}
          className="rounded-full border border-border-strong px-3 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken"
        >
          重新整理
        </button>
      </div>

      <LiabilityVsCashCard data={LIABILITY_VS_CASH_FIXTURE} />

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {DASHBOARD_KPIS.map((kpi) => (
          <KpiTile key={kpi.key} label={kpi.label} value="尚未提供" hint={kpi.hint} />
        ))}
      </div>

      <div className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">最近分錄</h2>
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

        <DataTable
          columns={columns}
          rows={filteredRows}
          getRowKey={(row) => row.id}
          onRowClick={setSelectedEntry}
          emptyTitle="這段期間沒有分錄"
          emptyDescription="換個日期範圍或清空來源模組篩選看看。"
        />
      </div>

      <Drawer
        open={selectedEntry !== null}
        onClose={() => setSelectedEntry(null)}
        title={selectedEntry ? `分錄：${selectedEntry.sourceRef}` : ''}
      >
        {selectedEntry ? (
          <div className="flex flex-col gap-4">
            <div>
              <p className="text-xs text-fg-muted">摘要</p>
              <p className="text-sm text-fg">{selectedEntry.memo}</p>
            </div>
            <div>
              <p className="text-xs text-fg-muted">分錄一經 posted 即不可修改</p>
              <p className="text-sm text-fg">更正只能開反向分錄，這裡沒有編輯也沒有刪除。</p>
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
                {relatedLines.map((line) => (
                  <tr key={line.id} className="border-b border-border-soft last:border-0">
                    <td className="py-2">{line.accountName}</td>
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
