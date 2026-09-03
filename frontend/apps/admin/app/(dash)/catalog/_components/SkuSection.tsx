'use client';

import { formatMoney } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { DataTable, MoneyCell, StatusPill, type DataTableColumn } from '@greygray/ui/admin';
import { fulfillmentModeLabel } from '../_lib/labels';
import { Button } from './Button';

type S = components['schemas'];

export interface SkuSectionProps {
  readonly mode: S['FulfillmentMode'];
  readonly skus: readonly S['AdminSku'][];
  readonly loading: boolean;
  readonly canWrite: boolean;
  readonly onAdd: () => void;
  readonly onEdit: (sku: S['AdminSku']) => void;
  readonly onReceive: (sku: S['AdminSku']) => void;
}

/**
 * 商品頁的 SKU 區。**純呈現**（狀態與 API 都在 `page.tsx`）——
 * 這個 workspace 沒有 jsdom，頁面本身用了 `useParams()` 也 render 不起來，
 * 拆出來之後「沒有寫入權就不該有按鈕」這件事才驗得到（見 `__tests__/skuSection.test.tsx`）。
 */
export function SkuSection({ mode, skus, loading, canWrite, onAdd, onEdit, onReceive }: SkuSectionProps) {
  const columns: DataTableColumn<S['AdminSku']>[] = [
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

  if (canWrite) {
    columns.push({
      key: 'actions',
      header: '操作',
      headerAlign: 'right',
      renderCell: (row) => (
        <td className="px-3 py-2 text-right">
          <Button
            variant="secondary"
            onClick={(event) => {
              // 這一列本身是「點開編輯」，按鈕不能連帶把編輯抽屜也開起來。
              event.stopPropagation();
              onReceive(row);
            }}
          >
            進貨
          </Button>
        </td>
      ),
    });
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">SKU（{fulfillmentModeLabel(mode)}）</h2>
        {canWrite ? (
          <Button variant="primary" onClick={onAdd}>
            新增 SKU
          </Button>
        ) : null}
      </div>
      <DataTable
        columns={columns}
        rows={skus}
        getRowKey={(row) => row.id}
        loading={loading}
        {...(canWrite ? { onRowClick: onEdit } : {})}
        emptyTitle="這個商品還沒有 SKU"
        emptyDescription={
          canWrite
            ? '還沒有 SKU——點右上角「新增 SKU」。沒有 SKU 就沒有價格與庫存，前台看不到這個商品。'
            : '還沒有 SKU。新增 SKU 需要 Operator 以上的權限。'
        }
      />
    </div>
  );
}
