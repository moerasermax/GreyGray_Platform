'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import { listOrders } from '@greygray/api-client/endpoints/admin';
import {
  DataTable,
  ErrorState,
  Field,
  FilterBar,
  MoneyCell,
  Select,
  StatusPill,
  useToast,
  type DataTableColumn,
} from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { usePayloadIdempotency } from '../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../login/_lib/session';
import { createShipment, listShipments, type CreateShipmentRequest, type ListShipmentsQuery } from './_lib/api';
import { shipmentMethodLabel, shipmentStatusLabel, shipmentStatusTone } from './_lib/labels';
import { Button } from './_components/Button';
import { CreateShipmentDialog } from './_components/CreateShipmentDialog';
import { ShipmentOrderLinksCell } from './_components/ShipmentOrderLinksCell';

type S = components['schemas'];
type Shipment = S['AdminShipment'];

const STATUS_OPTIONS: readonly S['ShipmentStatus'][] = [
  'Draft',
  'Packed',
  'Dispatched',
  'InTransit',
  'ArrivedAtStore',
  'Delivered',
  'Returned',
  'Lost',
];

export default function ShipmentsPage() {
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  const [status, setStatus] = useState('');
  const [rows, setRows] = useState<readonly Shipment[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [createOpen, setCreateOpen] = useState(false);
  const [orderNumberByOrderId, setOrderNumberByOrderId] = useState<ReadonlyMap<string, string>>(new Map());
  const [ordersLoadFailed, setOrdersLoadFailed] = useState(false);

  function buildQuery(cursor?: string): ListShipmentsQuery {
    return {
      ...(status ? { status: status as S['ShipmentStatus'] } : {}),
      ...(cursor ? { cursor } : {}),
      limit: 20,
    };
  }

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listShipments(browserApi(), buildQuery())
      .then((page) => {
        if (cancelled) return;
        setRows(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取出貨單列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [status, reloadKey]);

  // 出貨單掛的是哪一張訂單（#48）：契約沒有「依出貨單反查訂單」的端點，這裡撈一頁
  // 訂單在前端組 map；跟出貨單列表本身的抓取分開，失敗只讓「訂單」欄退化，不擋整個
  // 列表（做法照 `[shipmentId]/page.tsx` 第 67-82 行）。M1b 資料量吃得下 limit:100
  // 這個刻意的取捨，只影響訂單編號查得到查不到，不影響 orderIds 本身（那是出貨單
  // 自己的欄位），所以不會出現騙人的總數。
  useEffect(() => {
    let cancelled = false;
    void listOrders(browserApi(), { limit: 100 }).then(
      (page) => {
        if (cancelled) return;
        setOrderNumberByOrderId(new Map(page.items.map((order) => [order.id, order.orderNumber])));
      },
      () => {
        if (!cancelled) setOrdersLoadFailed(true);
      },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listShipments(browserApi(), buildQuery(nextCursor));
      setRows((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error('讀取下一頁失敗。'));
    } finally {
      setLoadingMore(false);
    }
  }

  async function handleCreate(input: CreateShipmentRequest) {
    const payload = { kind: 'create-shipment', input };
    await createShipment(browserApi(), input, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    toast.show('success', `已建立出貨單，合併 ${input.orderIds.length} 張訂單。`);
    setReloadKey((current) => current + 1);
  }

  const columns: DataTableColumn<Shipment>[] = [
    {
      key: 'id',
      header: '出貨單',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <Link href={`/shipments/${row.id}`} className="gg-numeric font-medium text-primary-text hover:underline">
            {row.id.slice(0, 8)}
          </Link>
        </td>
      ),
    },
    {
      key: 'orderIds',
      header: '訂單',
      renderCell: (row) => (
        <ShipmentOrderLinksCell
          orderIds={row.orderIds}
          orderNumberByOrderId={orderNumberByOrderId}
          ordersLoadFailed={ordersLoadFailed}
        />
      ),
    },
    {
      key: 'method',
      header: '配送方式',
      renderCell: (row) => <td className="px-3 py-2 text-fg-muted">{shipmentMethodLabel(row.method)}</td>,
    },
    {
      key: 'status',
      header: '狀態',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={shipmentStatusLabel(row.status)} tone={shipmentStatusTone(row.status)} />
        </td>
      ),
    },
    {
      key: 'trackingNumber',
      header: '追蹤單號',
      renderCell: (row) => (
        <td className="gg-numeric px-3 py-2 text-fg-muted">{row.trackingNumber ?? '—'}</td>
      ),
    },
    {
      key: 'carrierCost',
      header: '物流成本',
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={row.carrierCost ? formatMoney(row.carrierCost) : '—'} />,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-fg">出貨</h1>

      <FilterBar
        actions={
          canWrite ? (
            <Button variant="primary" onClick={() => setCreateOpen(true)}>
              ＋ 建立出貨單
            </Button>
          ) : null
        }
      >
        <Field label="狀態" htmlFor="shipments-filter-status">
          <Select
            id="shipments-filter-status"
            placeholder="全部狀態"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
            options={STATUS_OPTIONS.map((value) => ({ value, label: shipmentStatusLabel(value) }))}
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
            emptyTitle="沒有符合條件的出貨單"
            emptyDescription="換個狀態篩選，或建立一張新的出貨單。"
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

      <CreateShipmentDialog open={createOpen} onClose={() => setCreateOpen(false)} onConfirm={handleCreate} />
    </div>
  );
}
