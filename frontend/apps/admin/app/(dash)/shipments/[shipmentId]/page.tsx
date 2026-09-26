'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import { listOrders } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { ErrorState, StatusPill, useToast } from '@greygray/ui/admin';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../login/_lib/session';
import { deliverShipment, dispatchShipment, listShipments, type DispatchShipmentRequest } from '../_lib/api';
import { shipmentMethodLabel, shipmentStatusLabel, shipmentStatusTone } from '../_lib/labels';
import { Button } from '../_components/Button';
import { DeliverConfirmDialog } from '../_components/DeliverConfirmDialog';
import { DispatchShipmentDialog } from '../_components/DispatchShipmentDialog';

type S = components['schemas'];

const FINAL_STATUSES: ReadonlySet<S['ShipmentStatus']> = new Set(['Delivered', 'Returned', 'Lost']);

/**
 * 契約沒有 `GET /v1/shipments/{id}`——只有列表。這裡撈一份夠大的列表在前端找目標，
 * 不是發明一個契約沒有的端點（已回報整合者，見 `docs/15` §7 自驗交付）。
 */
export default function ShipmentDetailPage() {
  const params = useParams<{ shipmentId: string }>();
  const shipmentId = params.shipmentId;
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  const [shipment, setShipment] = useState<S['AdminShipment'] | null>(null);
  const [orderNumbers, setOrderNumbers] = useState<ReadonlyMap<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [dispatchOpen, setDispatchOpen] = useState(false);
  const [deliverOpen, setDeliverOpen] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listShipments(browserApi(), { limit: 100 })
      .then((page) => {
        if (cancelled) return;
        const found = page.items.find((s) => s.id === shipmentId) ?? null;
        if (!found) {
          setError(new Error('找不到這張出貨單。'));
          return;
        }
        setShipment(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取出貨單失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [shipmentId, reloadKey]);

  useEffect(() => {
    if (!shipment) return;
    let cancelled = false;
    void listOrders(browserApi(), { limit: 100 }).then(
      (page) => {
        if (cancelled) return;
        setOrderNumbers(new Map(page.items.map((order) => [order.id, order.orderNumber])));
      },
      () => {
        // 訂單編號拿不到就顯示原始 id，不擋整頁。
      },
    );
    return () => {
      cancelled = true;
    };
  }, [shipment]);

  async function handleDispatch(input: DispatchShipmentRequest) {
    if (!shipment) return;
    const payload = { shipmentId, kind: 'dispatch', input };
    const updated = await dispatchShipment(browserApi(), shipmentId, input, {
      idempotencyKey: idempotency.current(payload),
    });
    idempotency.complete();
    setShipment(updated);
    toast.show('success', '已交運。');
  }

  async function handleDeliver() {
    if (!shipment) return;
    const payload = { shipmentId, kind: 'deliver' };
    const updated = await deliverShipment(browserApi(), shipmentId, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    setShipment(updated);
    toast.show('success', '已標記為送達。');
  }

  if (loading) {
    return <div className="h-40 animate-pulse rounded-card bg-surface-sunken" />;
  }

  if (error || !shipment) {
    return (
      <ErrorState
        title={error instanceof ApiError ? error.problem.title : (error?.message ?? '找不到這張出貨單。')}
        traceId={error instanceof ApiError ? error.shortTraceId : null}
        onRetry={() => setReloadKey((current) => current + 1)}
      />
    );
  }

  const isFinal = FINAL_STATUSES.has(shipment.status);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/shipments" className="text-sm text-primary-text hover:underline">
            ← 回出貨單列表
          </Link>
          <h1 className="mt-1 gg-numeric text-xl font-semibold text-fg">{shipment.id.slice(0, 8)}</h1>
          <div className="mt-1 flex items-center gap-2">
            <StatusPill label={shipmentStatusLabel(shipment.status)} tone={shipmentStatusTone(shipment.status)} />
            <span className="text-sm text-fg-muted">{shipmentMethodLabel(shipment.method)}</span>
          </div>
        </div>
        {canWrite && !isFinal ? (
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => setDispatchOpen(true)}>
              交運
            </Button>
            <Button variant="primary" onClick={() => setDeliverOpen(true)}>
              標記已送達
            </Button>
          </div>
        ) : null}
      </div>

      <section className="grid grid-cols-1 gap-4 rounded-card border border-border-soft bg-surface p-5 shadow-card sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <p className="text-xs text-fg-muted">追蹤單號</p>
          <p className="gg-numeric mt-1 text-sm font-medium text-fg">{shipment.trackingNumber ?? '—'}</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">物流成本</p>
          <p className="gg-numeric mt-1 text-sm font-semibold text-fg">
            {shipment.carrierCost ? formatMoney(shipment.carrierCost) : '—'}
          </p>
          <p className="mt-0.5 text-xs text-fg-muted">付給物流商的成本，不是向客人收的運費</p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">交運時間</p>
          <p className="mt-1 text-sm font-medium text-fg">
            {shipment.dispatchedAt
              ? new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(
                  new Date(shipment.dispatchedAt),
                )
              : '尚未交運'}
          </p>
        </div>
        <div>
          <p className="text-xs text-fg-muted">送達時間</p>
          <p className="mt-1 text-sm font-medium text-fg">
            {shipment.deliveredAt
              ? new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(
                  new Date(shipment.deliveredAt),
                )
              : '尚未送達'}
          </p>
        </div>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-fg-muted">
          包含訂單（{shipment.orderIds.length} 張{shipment.orderIds.length > 1 ? '，合併出貨' : ''}）
        </h2>
        <ul className="flex flex-col gap-2">
          {shipment.orderIds.map((orderId) => (
            <li
              key={orderId}
              className="flex items-center justify-between rounded-card border border-border-soft bg-surface px-4 py-2.5 shadow-card"
            >
              <Link href={`/orders/${orderId}`} className="gg-numeric font-medium text-primary-text hover:underline">
                {orderNumbers.get(orderId) ?? orderId}
              </Link>
            </li>
          ))}
        </ul>
      </section>

      <DispatchShipmentDialog
        open={dispatchOpen}
        shipment={shipment}
        onClose={() => setDispatchOpen(false)}
        onConfirm={handleDispatch}
      />
      <DeliverConfirmDialog
        open={deliverOpen}
        shipment={shipment}
        onClose={() => setDeliverOpen(false)}
        onConfirm={handleDeliver}
      />
    </div>
  );
}
