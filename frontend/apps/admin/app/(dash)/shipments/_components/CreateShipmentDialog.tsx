'use client';

import { listOrders } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { formatMoney } from '@greygray/api-client';
import { Dialog, Field, Input, Select } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { apiErrorMessage } from '../_lib/apiError';
import type { CreateShipmentRequest } from '../_lib/api';
import { shipmentMethodLabel } from '../_lib/labels';
import { Button } from './Button';

type S = components['schemas'];

const METHOD_OPTIONS: readonly S['DeliveryMethod'][] = ['ConvenienceStore', 'HomeDelivery', 'SelfPickup'];

export interface CreateShipmentDialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly onConfirm: (input: CreateShipmentRequest) => Promise<void>;
}

/**
 * 建立出貨單。**Order 與 Shipment 是 N:M**（契約 description 明寫「這是日常，不是邊緣案例」）：
 * 一張訂單可以拆進多張出貨單，一張出貨單也可以合併多張訂單一起出——所以這裡一律是
 * 「先勾多張訂單，再選配送方式」，不是「在某張訂單頁按出貨」那種一對一的做法。
 */
export function CreateShipmentDialog({ open, onClose, onConfirm }: CreateShipmentDialogProps) {
  const [q, setQ] = useState('');
  const [orders, setOrders] = useState<readonly S['AdminOrderListItem'][]>([]);
  const [loadingOrders, setLoadingOrders] = useState(false);
  const [selectedOrderIds, setSelectedOrderIds] = useState<readonly string[]>([]);
  const [method, setMethod] = useState<S['DeliveryMethod']>('ConvenienceStore');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setQ('');
      setOrders([]);
      setSelectedOrderIds([]);
      setMethod('ConvenienceStore');
      setError(null);
      return;
    }
    let cancelled = false;
    setLoadingOrders(true);
    const timer = setTimeout(() => {
      const trimmedQ = q.trim();
      void listOrders(browserApi(), { ...(trimmedQ ? { q: trimmedQ } : {}), limit: 50 })
        .then((page) => {
          if (!cancelled) setOrders(page.items);
        })
        .catch(() => {
          // 訂單清單拿不到就先讓勾選區是空的，不擋整個 Dialog。
        })
        .finally(() => {
          if (!cancelled) setLoadingOrders(false);
        });
    }, 300);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [open, q]);

  function toggleOrder(orderId: string) {
    setSelectedOrderIds((current) =>
      current.includes(orderId) ? current.filter((id) => id !== orderId) : [...current, orderId],
    );
  }

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleSubmit() {
    if (selectedOrderIds.length === 0) {
      setError('至少要勾選一張訂單——一張出貨單可以合併多張訂單，也可以只出一張。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ orderIds: selectedOrderIds, method });
      onClose();
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title="建立出貨單"
      description="先勾選要出的訂單（可複選，也可以合併不同訂單一起出），再選配送方式。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '建立中…' : `建立出貨單（已選 ${selectedOrderIds.length} 張）`}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error ? (
          <p role="alert" className="text-sm font-medium text-danger">
            {error}
          </p>
        ) : null}

        <Field label="搜尋訂單" htmlFor="create-shipment-q" hint="訂單編號或客戶姓名">
          <Input
            id="create-shipment-q"
            value={q}
            onChange={(event) => setQ(event.target.value)}
            placeholder="例如 GG26082800031 或王小美"
          />
        </Field>

        <div className="max-h-64 overflow-y-auto rounded-card border border-border-soft">
          {loadingOrders ? (
            <div className="p-4 text-sm text-fg-muted">載入訂單中…</div>
          ) : orders.length === 0 ? (
            <div className="p-4 text-sm text-fg-muted">沒有符合條件的訂單。</div>
          ) : (
            <ul>
              {orders.map((order) => {
                const inputId = `create-shipment-order-${order.id}`;
                const checked = selectedOrderIds.includes(order.id);
                return (
                  <li key={order.id} className="border-b border-border-soft last:border-0">
                    <label
                      htmlFor={inputId}
                      className={`flex min-h-11 cursor-pointer items-center justify-between gap-3 px-3 py-2 text-sm ${
                        checked ? 'bg-primary-subtle' : 'hover:bg-surface-sunken'
                      }`}
                    >
                      <span className="flex items-center gap-3">
                        <input
                          id={inputId}
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleOrder(order.id)}
                          className="h-4 w-4 accent-[var(--color-primary)]"
                        />
                        <span>
                          <span className="gg-numeric font-medium text-fg">{order.orderNumber}</span>
                          <span className="ml-2 text-fg-muted">{order.customerDisplayName}</span>
                        </span>
                      </span>
                      <span className="gg-numeric text-fg-muted">{formatMoney(order.grandTotal)}</span>
                    </label>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <Field label="配送方式" htmlFor="create-shipment-method" required>
          <Select
            id="create-shipment-method"
            value={method}
            onChange={(event) => setMethod(event.target.value as S['DeliveryMethod'])}
            options={METHOD_OPTIONS.map((value) => ({ value, label: shipmentMethodLabel(value) }))}
          />
        </Field>
      </div>
    </Dialog>
  );
}
