'use client';

import { ApiError } from '@greygray/api-client';
import { listCampaigns } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, ErrorState, Field, FilterBar, Select, StatusPill, type DataTableColumn } from '@greygray/ui/admin';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { getSession, hasRequiredRole } from '../../login/_lib/session';
import { Button } from './_components/Button';
import { campaignStatusLabel, campaignStatusTone } from './_lib/labels';

type S = components['schemas'];

const STATUS_OPTIONS: readonly S['CampaignStatus'][] = [
  'Draft',
  'Open',
  'Closed',
  'TripInProgress',
  'Returned',
  'Settled',
  'Cancelled',
];

export default function CampaignsPage() {
  const [status, setStatus] = useState('');
  const [rows, setRows] = useState<readonly S['AdminCampaign'][]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listCampaigns(browserApi(), {
      ...(status ? { status: status as S['CampaignStatus'] } : {}),
      limit: 20,
    })
      .then((page) => {
        if (cancelled) return;
        setRows(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取開團列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [status, reloadKey]);

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listCampaigns(browserApi(), {
        ...(status ? { status: status as S['CampaignStatus'] } : {}),
        cursor: nextCursor,
        limit: 20,
      });
      setRows((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error('讀取下一頁失敗。'));
    } finally {
      setLoadingMore(false);
    }
  }

  const columns: DataTableColumn<S['AdminCampaign']>[] = [
    {
      key: 'title',
      header: '團名',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <Link href={`/campaigns/${row.id}`} className="font-medium text-primary hover:underline">
            {row.title}
          </Link>
          <div className="text-xs text-fg-muted">{row.destination}</div>
        </td>
      ),
    },
    {
      key: 'status',
      header: '狀態',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={campaignStatusLabel(row.status)} tone={campaignStatusTone(row.status)} />
        </td>
      ),
    },
    {
      key: 'orderCount',
      header: '訂單數',
      headerAlign: 'right',
      renderCell: (row) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          {row.orderCount}
        </td>
      ),
    },
    {
      key: 'closesAt',
      header: '截團時間',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(row.closesAt))}
        </td>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-fg">開團管理</h1>
        {canWrite ? (
          <Link href="/campaigns/new">
            <Button variant="primary">新增開團</Button>
          </Link>
        ) : null}
      </div>

      <FilterBar>
        <Field label="狀態" htmlFor="campaigns-filter-status">
          <Select
            id="campaigns-filter-status"
            placeholder="全部狀態"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
            options={STATUS_OPTIONS.map((value) => ({ value, label: campaignStatusLabel(value) }))}
          />
        </Field>
      </FilterBar>

      {error ? (
        <ErrorState
          title={error instanceof ApiError ? error.problem.title : error.message}
          traceId={error instanceof ApiError ? error.shortTraceId : null}
          onRetry={() => {
            setError(null);
            setReloadKey((current) => current + 1);
          }}
        />
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={rows}
            getRowKey={(row) => row.id}
            loading={loading}
            emptyTitle="沒有符合條件的開團"
            emptyDescription="換個狀態篩選看看，或建立一個新的開團。"
          />
          {nextCursor ? (
            <div className="flex justify-center">
              <Button variant="secondary" onClick={() => void handleLoadMore()} disabled={loadingMore}>
                {loadingMore ? '載入中…' : '載入更多'}
              </Button>
            </div>
          ) : null}
        </>
      )}
    </div>
  );
}
