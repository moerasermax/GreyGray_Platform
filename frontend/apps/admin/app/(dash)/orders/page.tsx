'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import { listCampaigns, listOrders } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import {
  DataTable,
  ErrorState,
  Field,
  FilterBar,
  Input,
  MoneyCell,
  Select,
  StatusPill,
  type DataTableColumn,
} from '@greygray/ui/admin';
import Link from 'next/link';
import { useEffect, useMemo, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { getSession, hasRequiredRole } from '../../login/_lib/session';
import { listShipments } from '../shipments/_lib/api';
import { ShipmentProgressCell } from './_components/ShipmentProgressCell';
import { orderStatusLabel, orderStatusTone } from './_lib/labels';
import type { ListAdminOrdersQuery } from '@greygray/api-client/endpoints/admin';

type S = components['schemas'];
type OrderListItem = S['AdminOrderListItem'];

const ORDER_STATUS_OPTIONS: readonly S['OrderStatus'][] = [
  'AwaitingPayment',
  'PaidAwaitingClose',
  'ClosedAwaitingDeparture',
  'Purchasing',
  'GoodsReceived',
  'ReadyToShip',
  'Shipped',
  'Completed',
  'Cancelled',
];

export default function OrdersPage() {
  // 出貨單清單（`GET /v1/shipments`）後端掛 `StaffRoleFilter(Operator)`：不符合 Operator 的
  // 角色（唯讀、會計）打了就 403。角色不符時不打，「出貨進度」欄顯示中性的「—」（FE-50）。
  // `(dash)/layout.tsx` 拿到員工資料前不渲染子頁，所以 `getSession()` 這裡一定有值。
  const canViewShipments = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  const [q, setQ] = useState('');
  const [debouncedQ, setDebouncedQ] = useState('');
  const [status, setStatus] = useState('');
  const [campaignId, setCampaignId] = useState('');
  const [campaigns, setCampaigns] = useState<readonly S['AdminCampaign'][]>([]);

  const [rows, setRows] = useState<readonly OrderListItem[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // 出貨進度欄（#49）。契約沒有「依訂單查出貨單」的篩選，撈一頁在前端用 `orderIds`
  // 比對——同一個模式在 `[orderId]/page.tsx` 第 60-63 行、`shipments/[shipmentId]/page.tsx`
  // 第 24-26 行已經用了。一次抓、前端比對，不對每一列各打一次 API（訂單列表有分頁，
  // 那會變成 N+1）；有下一頁時文案自己講「可能不完整」，不假裝張數是總數。
  const [shipments, setShipments] = useState<readonly S['AdminShipment'][]>([]);
  const [shipmentsFailed, setShipmentsFailed] = useState(false);
  const [shipmentsTruncated, setShipmentsTruncated] = useState(false);

  // 搜尋框打字去抖動，避免每個按鍵都打一次 API。
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQ(q.trim()), 300);
    return () => clearTimeout(timer);
  }, [q]);

  useEffect(() => {
    if (!canViewShipments) return;
    let cancelled = false;
    void listShipments(browserApi(), { limit: 100 }).then(
      (page) => {
        if (cancelled) return;
        setShipments(page.items);
        setShipmentsTruncated(page.nextCursor != null);
      },
      () => {
        if (!cancelled) setShipmentsFailed(true);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [canViewShipments]);

  useEffect(() => {
    let cancelled = false;
    void listCampaigns(browserApi(), { limit: 100 }).then(
      (page) => {
        if (!cancelled) setCampaigns(page.items);
      },
      () => {
        // 篩選用的下拉選單，拿不到就留空，不擋主要的訂單列表。
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
    void listOrders(browserApi(), buildQuery())
      .then((page) => {
        if (cancelled) return;
        setRows(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取訂單列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [debouncedQ, status, campaignId, reloadKey]);

  function buildQuery(cursor?: string): ListAdminOrdersQuery {
    return {
      ...(debouncedQ ? { q: debouncedQ } : {}),
      ...(status ? { status: status as S['OrderStatus'] } : {}),
      ...(campaignId ? { campaignId } : {}),
      ...(cursor ? { cursor } : {}),
      limit: 20,
    };
  }

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listOrders(browserApi(), buildQuery(nextCursor));
      setRows((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error('讀取下一頁失敗。'));
    } finally {
      setLoadingMore(false);
    }
  }

  const campaignOptions = useMemo(
    () => campaigns.map((c) => ({ value: c.id, label: c.title })),
    [campaigns],
  );

  const columns: DataTableColumn<OrderListItem>[] = [
    {
      key: 'orderNumber',
      header: '訂單編號',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <Link href={`/orders/${row.id}`} className="gg-numeric font-medium text-primary-text hover:underline">
            {row.orderNumber}
          </Link>
        </td>
      ),
    },
    {
      key: 'customerDisplayName',
      header: '客戶',
      renderCell: (row) => <td className="px-3 py-2 text-fg">{row.customerDisplayName}</td>,
    },
    {
      key: 'status',
      header: '狀態',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={orderStatusLabel(row.status)} tone={orderStatusTone(row.status)} />
        </td>
      ),
    },
    {
      key: 'shipmentProgress',
      header: '出貨進度',
      renderCell: (row) =>
        canViewShipments ? (
          <ShipmentProgressCell
            orderId={row.id}
            orderStatus={row.status}
            shipments={shipmentsFailed ? null : shipments}
            truncated={shipmentsTruncated}
          />
        ) : (
          // 沒有出貨單的讀取權限：不是「載入失敗」，也不是「未建立」，就是不知道。
          <td className="px-3 py-2 text-fg-muted">—</td>
        ),
    },
    {
      key: 'campaignId',
      header: '所屬團',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {row.campaignId ? (campaigns.find((c) => c.id === row.campaignId)?.title ?? row.campaignId) : '—'}
        </td>
      ),
    },
    {
      key: 'grandTotal',
      header: '含運總額',
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={formatMoney(row.grandTotal)} />,
    },
    {
      key: 'placedAt',
      header: '下單時間',
      renderCell: (row) => (
        <td className="px-3 py-2 text-fg-muted">
          {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(row.placedAt))}
        </td>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-fg">訂單</h1>

      <FilterBar>
        <Field label="搜尋" htmlFor="orders-filter-q" hint="訂單編號或客戶姓名">
          <Input
            id="orders-filter-q"
            value={q}
            onChange={(event) => setQ(event.target.value)}
            placeholder="例如 GG26082800031 或王小美"
          />
        </Field>
        <Field label="狀態" htmlFor="orders-filter-status">
          <Select
            id="orders-filter-status"
            placeholder="全部狀態"
            value={status}
            onChange={(event) => setStatus(event.target.value)}
            options={ORDER_STATUS_OPTIONS.map((value) => ({ value, label: orderStatusLabel(value) }))}
          />
        </Field>
        <Field label="開團" htmlFor="orders-filter-campaign">
          <Select
            id="orders-filter-campaign"
            placeholder="全部（含非團購）"
            value={campaignId}
            onChange={(event) => setCampaignId(event.target.value)}
            options={campaignOptions}
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
            emptyTitle="沒有符合條件的訂單"
            emptyDescription="換個關鍵字或篩選條件看看。"
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
    </div>
  );
}
