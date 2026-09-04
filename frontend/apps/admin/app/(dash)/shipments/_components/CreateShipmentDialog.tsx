'use client';

import { listOrders } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { formatMoney } from '@greygray/api-client';
import { Dialog, Field, Input, Select } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { apiErrorMessage } from '../_lib/apiError';
import { listShipments, type CreateShipmentRequest } from '../_lib/api';
import { shipmentMethodLabel } from '../_lib/labels';
import { countShipmentsByOrderId, selectableOrdersForShipment } from '../_lib/orderShipments';
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
  /** 端點回了幾張（過濾前）。用來分辨「搜尋不到」與「搜到了但都不能再出貨」。 */
  const [fetchedCount, setFetchedCount] = useState(0);
  /** 每張訂單已經掛了幾張出貨單。拿不到就是空的 map，只是少一行標示，不擋建立。 */
  const [shipmentCounts, setShipmentCounts] = useState<ReadonlyMap<string, number>>(new Map());
  const [loadingOrders, setLoadingOrders] = useState(false);
  const [selectedOrderIds, setSelectedOrderIds] = useState<readonly string[]>([]);
  const [method, setMethod] = useState<S['DeliveryMethod']>('ConvenienceStore');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setQ('');
      setOrders([]);
      setFetchedCount(0);
      setShipmentCounts(new Map());
      setSelectedOrderIds([]);
      setMethod('ConvenienceStore');
      setError(null);
      return;
    }
    let cancelled = false;
    setLoadingOrders(true);
    const timer = setTimeout(() => {
      const trimmedQ = q.trim();
      // `status` query 一次只吃一個值，而這裡要排除三個（已出貨／已完成／已取消），
      // 所以拿回來在前端過濾——規則與訂單頁共用 `_lib/orderShipments.ts` 那一份。
      void listOrders(browserApi(), { ...(trimmedQ ? { q: trimmedQ } : {}), limit: 50 })
        .then((page) => {
          if (cancelled) return;
          setFetchedCount(page.items.length);
          setOrders(selectableOrdersForShipment(page.items));
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

  // 「這張訂單已經有幾張出貨單」。契約沒有依訂單篩選出貨單的參數，撈一頁在前端用
  // `orderIds` 比對（同 `[shipmentId]/page.tsx` 第 46-49 行的模式）；M1b 的資料量
  // 吃得下，而且這只是一行提示，少算到不會擋任何操作。
  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    void listShipments(browserApi(), { limit: 100 }).then(
      (page) => {
        if (!cancelled) setShipmentCounts(countShipmentsByOrderId(page.items));
      },
      () => {
        // 拿不到就不標示，不擋建立出貨單。
      },
    );
    return () => {
      cancelled = true;
    };
  }, [open]);

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
        <p className="rounded-card border border-border-soft bg-surface-sunken px-3 py-2 text-xs text-fg-muted">
          一張出貨單可以合併多張訂單，一張訂單也可以拆進多張出貨單；
          <span className="font-semibold text-fg">訂單要等它掛的出貨單全部簽收，才會變成「已出貨」。</span>
          已出貨、已完成、已取消的訂單不會列在下面。
        </p>

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
            <div className="p-4 text-sm text-fg-muted">
              {fetchedCount > 0
                ? '目前沒有可以出貨的訂單——找到的訂單都已經出貨、已完成或已取消了。'
                : '沒有符合條件的訂單。'}
            </div>
          ) : (
            <ul>
              {orders.map((order) => {
                const inputId = `create-shipment-order-${order.id}`;
                const checked = selectedOrderIds.includes(order.id);
                // 已經有出貨單的**仍然可以勾**（拆單合法，ADR-025），只是標示出來
                // 讓操作的人自己判斷這次是不是真的要再拆一張。
                const existingShipments = shipmentCounts.get(order.id) ?? 0;
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
                          {existingShipments > 0 ? (
                            <span className="ml-2 rounded-full bg-surface-sunken px-2 py-0.5 text-xs text-fg-muted">
                              已有 {existingShipments} 張出貨單
                            </span>
                          ) : null}
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
