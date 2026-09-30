'use client';

import { ApiError } from '@greygray/api-client';
import { listCategories, listProducts } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, ErrorState, Field, FilterBar, Input, Select, StatusPill, type DataTableColumn } from '@greygray/ui/admin';
import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { getSession, hasRequiredRole } from '../../login/_lib/session';
import { Button } from './_components/Button';
import { CatalogTabs } from './_components/CatalogTabs';
import { categoryOptions as buildCategoryOptions } from './_components/CategoryDialog';
import { fulfillmentModeLabel } from './_lib/labels';

type S = components['schemas'];
type ProductRow = S['AdminProduct'];

export default function CatalogProductsPage() {
  const [q, setQ] = useState('');
  const [debouncedQ, setDebouncedQ] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [categories, setCategories] = useState<readonly S['Category'][]>([]);

  const [rows, setRows] = useState<readonly ProductRow[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQ(q.trim()), 300);
    return () => clearTimeout(timer);
  }, [q]);

  useEffect(() => {
    let cancelled = false;
    void listCategories(browserApi()).then(
      (items) => {
        if (!cancelled) setCategories(items);
      },
      () => {
        // 篩選用的下拉選單，拿不到就留空，不擋主要的商品列表。
      },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listProducts(browserApi(), {
      ...(debouncedQ ? { q: debouncedQ } : {}),
      ...(categoryId ? { categoryId } : {}),
      includeArchived,
      limit: 20,
    })
      .then((page) => {
        if (cancelled) return;
        setRows(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取商品列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [debouncedQ, categoryId, includeArchived, reloadKey]);

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listProducts(browserApi(), {
        ...(debouncedQ ? { q: debouncedQ } : {}),
        ...(categoryId ? { categoryId } : {}),
        includeArchived,
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

  const categoryOptions = useMemo(
    () => buildCategoryOptions(categories),
    [categories],
  );

  const columns: DataTableColumn<ProductRow>[] = [
    {
      key: 'name',
      header: '商品名稱',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <Link href={`/catalog/products/${row.id}`} className="font-medium text-primary-text hover:underline">
            {row.name}
          </Link>
          {row.shortDescription ? <div className="text-xs text-fg-muted">{row.shortDescription}</div> : null}
        </td>
      ),
    },
    {
      key: 'category',
      header: '分類',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {categories.find((category) => category.id === row.categoryId)?.name ?? '未分類'}
        </td>
      ),
    },
    {
      key: 'mode',
      header: '模式',
      renderCell: (row) => <td className="px-3 py-2 text-fg">{fulfillmentModeLabel(row.mode)}</td>,
    },
    {
      key: 'skus',
      header: 'SKU 數',
      headerAlign: 'right',
      renderCell: (row) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          {row.skus.length}
        </td>
      ),
    },
    {
      key: 'isActive',
      header: '狀態',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={row.isActive ? '上架中' : '已下架'} tone={row.isActive ? 'success' : 'neutral'} />
        </td>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold text-fg">商品管理</h1>
        {canWrite ? (
          <Link href="/catalog/products/new">
            <Button variant="primary">新增商品</Button>
          </Link>
        ) : null}
      </div>

      <CatalogTabs />

      <FilterBar>
        <Field label="搜尋" htmlFor="products-filter-q" hint="商品名稱">
          <Input
            id="products-filter-q"
            value={q}
            onChange={(event) => setQ(event.target.value)}
            placeholder="例如 森田藥粧"
          />
        </Field>
        <Field label="分類" htmlFor="products-filter-category">
          <Select
            id="products-filter-category"
            placeholder="全部分類"
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
            options={categoryOptions}
          />
        </Field>
        <label className="flex items-center gap-2 pb-1.5 text-sm text-fg">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(event) => setIncludeArchived(event.target.checked)}
            className="h-4 w-4 rounded-sm border-border-strong"
          />
          包含已下架商品
        </label>
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
            emptyTitle="沒有符合條件的商品"
            emptyDescription="換個關鍵字或篩選條件看看。"
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
