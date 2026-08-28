'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import { getProduct, listCategories, updateProduct } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, ErrorState, MoneyCell, StatusPill, type DataTableColumn } from '@greygray/ui/admin';
import { useParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../../login/_lib/session';
import { ProductForm } from '../../_components/ProductForm';
import { SkuEditDrawer } from '../../_components/SkuEditDrawer';
import { fulfillmentModeLabel } from '../../_lib/labels';

type S = components['schemas'];

export default function ProductDetailPage() {
  const params = useParams<{ productId: string }>();
  const productId = params.productId;
  const idempotency = usePayloadIdempotency();

  const [product, setProduct] = useState<S['AdminProduct'] | null>(null);
  const [categories, setCategories] = useState<readonly S['Category'][]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [editingSku, setEditingSku] = useState<S['AdminSku'] | null>(null);

  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  useEffect(() => {
    let cancelled = false;
    void listCategories(browserApi()).then(
      (items) => {
        if (!cancelled) setCategories(items);
      },
      () => {},
    );
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void getProduct(browserApi(), productId)
      .then((found) => {
        if (!cancelled) setProduct(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取商品失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [productId, reloadKey]);

  async function handleSubmit(body: S['AdminProductInput']) {
    const payload = { productId, body };
    await updateProduct(browserApi(), productId, body, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    // 寫入完成後重新 GET，不要拿 request 內容當成新狀態（docs/05 §9）。
    setReloadKey((current) => current + 1);
  }

  const skuColumns: DataTableColumn<S['AdminSku']>[] = [
    {
      key: 'name',
      header: 'SKU',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <div className="font-medium text-fg">{row.name}</div>
          {row.variantName ? <div className="text-xs text-fg-muted">{row.variantName}</div> : null}
        </td>
      ),
    },
    {
      key: 'weight',
      header: '重量／尺寸',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {row.weightGram} g · {row.size.lengthCm}×{row.size.widthCm}×{row.size.heightCm} cm
        </td>
      ),
    },
    {
      key: 'available',
      header: '可用量',
      headerAlign: 'right',
      renderCell: (row) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          {row.available}
        </td>
      ),
    },
    {
      key: 'listPrice',
      header: '現貨標價',
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={row.listPrice ? formatMoney(row.listPrice) : '—'} />,
    },
    {
      key: 'isActive',
      header: '狀態',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={row.isActive ? '啟用中' : '已停用'} tone={row.isActive ? 'success' : 'neutral'} />
        </td>
      ),
    },
  ];

  if (error) {
    return (
      <ErrorState
        title={error instanceof ApiError ? error.problem.title : error.message}
        traceId={error instanceof ApiError ? error.shortTraceId : null}
        onRetry={() => {
          setError(null);
          setReloadKey((current) => current + 1);
        }}
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold text-fg">{loading ? '載入中…' : product?.name}</h1>

      {product ? (
        <ProductForm
          key={reloadKey}
          initial={product}
          categories={categories}
          submitLabel="儲存商品資料"
          onSubmit={handleSubmit}
        />
      ) : null}

      <div className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">
          SKU（{fulfillmentModeLabel(product?.mode ?? 'Stock')}）
        </h2>
        <DataTable
          columns={skuColumns}
          rows={product?.skus ?? []}
          getRowKey={(row) => row.id}
          loading={loading}
          {...(canWrite ? { onRowClick: (row: S['AdminSku']) => setEditingSku(row) } : {})}
          emptyTitle="這個商品還沒有 SKU"
          emptyDescription="契約目前只有 PATCH /v1/skus/{skuId}（修改既有 SKU），沒有新增 SKU 的端點——這筆已經記進交付回報的契約問題，SKU 要等後端補上端點才能建立。"
        />
      </div>

      <SkuEditDrawer
        open={editingSku !== null}
        sku={editingSku}
        onClose={() => setEditingSku(null)}
        onSaved={() => setReloadKey((current) => current + 1)}
      />
    </div>
  );
}
