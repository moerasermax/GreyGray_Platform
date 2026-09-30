'use client';

import { ApiError } from '@greygray/api-client';
import { listCategories } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, ErrorState, type DataTableColumn } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { getSession, hasRequiredRole } from '../../../login/_lib/session';
import { CatalogTabs } from '../_components/CatalogTabs';
import { CategoryDialog, orderCategories } from '../_components/CategoryDialog';
import { Button } from '../_components/Button';

type S = components['schemas'];

export default function CategoriesPage() {
  const [rows, setRows] = useState<readonly S['Category'][]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<S['Category'] | null>(null);

  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listCategories(browserApi())
      .then((items) => {
        if (!cancelled) setRows(items);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取分類列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const columns: DataTableColumn<S['Category']>[] = [
    {
      key: 'name',
      header: '名稱',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg">{row.parentId ? `└ ${row.name}` : row.name}</td>
      ),
    },
    {
      key: 'parentId',
      header: '上層分類',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {row.parentId ? rows.find((category) => category.id === row.parentId)?.name ?? '—' : '—'}
        </td>
      ),
    },
    {
      key: 'sortOrder',
      header: '排序',
      headerAlign: 'right',
      renderCell: (row) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          {row.sortOrder}
        </td>
      ),
    },
    {
      key: 'imageUrl',
      header: '圖片',
      renderCell: (row) => <td className="px-3 py-2 text-fg-muted">{row.imageUrl ?? '—'}</td>,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-fg">商品管理</h1>
        {canWrite ? (
          <Button
            variant="primary"
            onClick={() => {
              setEditing(null);
              setDialogOpen(true);
            }}
          >
            新增分類
          </Button>
        ) : null}
      </div>

      <CatalogTabs />

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
        <DataTable
          columns={columns}
          rows={orderCategories(rows)}
          getRowKey={(row) => row.id}
          loading={loading}
          {...(canWrite
            ? {
                onRowClick: (row: S['Category']) => {
                  setEditing(row);
                  setDialogOpen(true);
                },
              }
            : {})}
          emptyTitle="還沒有任何分類"
          emptyDescription="分類是首頁橫捲圓形標籤的資料來源。"
          emptyAction={
            canWrite ? (
              <Button
                variant="primary"
                onClick={() => {
                  setEditing(null);
                  setDialogOpen(true);
                }}
              >
                新增分類
              </Button>
            ) : undefined
          }
        />
      )}

      <CategoryDialog
        open={dialogOpen}
        category={editing}
        categories={rows}
        onClose={() => setDialogOpen(false)}
        onSaved={() => setReloadKey((current) => current + 1)}
      />
    </div>
  );
}
