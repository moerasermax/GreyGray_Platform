'use client';

import { formatMoney, moneyFromMajorInput } from '@greygray/api-client';
import { addCampaignOffer, listProducts, removeCampaignOffer } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { DataTable, Dialog, Drawer, Field, Input, MoneyCell, Select, useToast, type DataTableColumn } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../login/_lib/session';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface OffersSectionProps {
  readonly campaignId: string;
  readonly offers: readonly S['AdminCampaignOffer'][];
  readonly loading: boolean;
  /** 加入／移除商品都要重新 GET 整個團，不要自己在前端拼陣列（docs/05 §9）。 */
  readonly onChanged: () => void;
}

interface SkuOption {
  readonly skuId: string;
  readonly label: string;
}

export function OffersSection({ campaignId, offers, loading, onChanged }: OffersSectionProps) {
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const role = getSession()?.role ?? 'ReadOnly';
  const canWrite = hasRequiredRole(role, 'Operator');

  const [skuOptions, setSkuOptions] = useState<readonly SkuOption[]>([]);
  const [addOpen, setAddOpen] = useState(false);
  const [selectedSkuId, setSelectedSkuId] = useState('');
  const [sellingPriceMajor, setSellingPriceMajor] = useState('');
  const [targetPriceMajor, setTargetPriceMajor] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [viewingOffer, setViewingOffer] = useState<S['AdminCampaignOffer'] | null>(null);
  const [removeTarget, setRemoveTarget] = useState<S['AdminCampaignOffer'] | null>(null);
  const [removeError, setRemoveError] = useState<string | null>(null);
  const [removing, setRemoving] = useState(false);

  useEffect(() => {
    if (!addOpen) return;
    let cancelled = false;
    void listProducts(browserApi(), { includeArchived: false, limit: 100 }).then(
      (page) => {
        if (cancelled) return;
        const options = page.items.flatMap((product) =>
          product.skus.map((sku) => ({
            skuId: sku.id,
            label: `${product.name}${sku.variantName ? `．${sku.variantName}` : ''}`,
          })),
        );
        setSkuOptions(options);
      },
      () => {
        // 商品清單拿不到就先讓下拉是空的，不擋整個 Dialog。
      },
    );
    return () => {
      cancelled = true;
    };
  }, [addOpen]);

  async function handleAddOffer() {
    const sellingPrice = moneyFromMajorInput(sellingPriceMajor, 'TWD');
    const targetPurchasePrice = targetPriceMajor.trim()
      ? moneyFromMajorInput(targetPriceMajor, 'TWD')
      : null;
    if (!selectedSkuId || !sellingPrice || (targetPriceMajor.trim() && !targetPurchasePrice)) {
      setFormError('請選擇 SKU 並填寫售價。');
      return;
    }
    setSubmitting(true);
    setFormError(null);
    try {
      const body = {
        skuId: selectedSkuId,
        sellingPrice,
        targetPurchasePrice,
      } satisfies Parameters<typeof addCampaignOffer>[2];
      const payload = { campaignId, kind: 'add', body };
      await addCampaignOffer(browserApi(), campaignId, body, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      toast.show('success', '已加入開團商品。');
      setAddOpen(false);
      setSelectedSkuId('');
      setSellingPriceMajor('');
      setTargetPriceMajor('');
      onChanged();
    } catch (cause) {
      setFormError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleRemoveConfirm() {
    if (!removeTarget) return;
    setRemoving(true);
    setRemoveError(null);
    try {
      const payload = { campaignId, kind: 'remove', offerId: removeTarget.id };
      await removeCampaignOffer(browserApi(), campaignId, removeTarget.id, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      toast.show('success', '已移除開團商品。');
      setRemoveTarget(null);
      onChanged();
    } catch (cause) {
      // 已經有訂單引用時後端回 422（`campaign.offer-has-orders`），訊息原樣顯示在這裡而不是默默吞掉。
      setRemoveError(apiErrorMessage(cause));
    } finally {
      setRemoving(false);
    }
  }

  const columns: DataTableColumn<S['AdminCampaignOffer']>[] = [
    {
      key: 'name',
      header: '商品',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <button type="button" className="font-medium text-primary hover:underline" onClick={() => setViewingOffer(row)}>
            {row.name}
          </button>
          {row.variantName ? <div className="text-xs text-fg-muted">{row.variantName}</div> : null}
        </td>
      ),
    },
    {
      key: 'sellingPrice',
      header: '售價',
      headerAlign: 'right',
      renderCell: (row) => <MoneyCell value={formatMoney(row.sellingPrice)} />,
    },
    {
      key: 'targetPurchasePrice',
      header: '目標採購價',
      headerAlign: 'right',
      renderCell: (row) => (
        <MoneyCell value={row.targetPurchasePrice ? formatMoney(row.targetPurchasePrice) : '—'} />
      ),
    },
    {
      key: 'orderedQuantity',
      header: '已下單數量',
      headerAlign: 'right',
      renderCell: (row) => (
        <td data-numeric className="gg-numeric px-3 py-2">
          {row.orderedQuantity}
        </td>
      ),
    },
    ...(canWrite
      ? [
          {
            key: 'actions',
            header: '',
            renderCell: (row: S['AdminCampaignOffer']) => (
              <td className="px-3 py-2 text-right">
                <Button variant="secondary" onClick={() => setRemoveTarget(row)}>
                  移除
                </Button>
              </td>
            ),
          },
        ]
      : []),
  ];

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-fg-muted">開團商品</h2>
        {canWrite ? (
          <Button variant="primary" onClick={() => setAddOpen(true)}>
            新增開團商品
          </Button>
        ) : null}
      </div>

      <DataTable
        columns={columns}
        rows={offers}
        getRowKey={(row) => row.id}
        loading={loading}
        emptyTitle="這個團還沒有任何商品"
        emptyDescription="新增開團商品後，售價就會定死——發布之後不能再改。"
      />

      <Dialog
        open={addOpen}
        onClose={() => setAddOpen(false)}
        title="新增開團商品"
        description="sellingPrice 一經加入即定死，之後契約沒有提供修改端點。"
        footer={
          <>
            <Button variant="secondary" onClick={() => setAddOpen(false)} disabled={submitting}>
              取消
            </Button>
            <Button variant="primary" onClick={() => void handleAddOffer()} disabled={submitting || !selectedSkuId}>
              {submitting ? '加入中…' : '加入'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {formError ? (
            <p role="alert" className="text-sm font-medium text-danger">
              {formError}
            </p>
          ) : null}
          <Field label="SKU" htmlFor="offer-sku" required>
            <Select
              id="offer-sku"
              placeholder="選擇商品 SKU"
              value={selectedSkuId}
              onChange={(event) => setSelectedSkuId(event.target.value)}
              options={skuOptions.map((option) => ({ value: option.skuId, label: option.label }))}
            />
          </Field>
          <Field label="售價（元）" htmlFor="offer-selling-price" required hint="發布後不可再改">
            <Input
              id="offer-selling-price"
              type="number"
              min={0}
              value={sellingPriceMajor}
              onChange={(event) => setSellingPriceMajor(event.target.value)}
            />
          </Field>
          <Field label="目標採購價（元）" htmlFor="offer-target-price" hint="選填，只是自己看的參考，現場買貴買便宜都不影響已成立的訂單">
            <Input
              id="offer-target-price"
              type="number"
              min={0}
              value={targetPriceMajor}
              onChange={(event) => setTargetPriceMajor(event.target.value)}
            />
          </Field>
        </div>
      </Dialog>

      <Drawer open={viewingOffer !== null} onClose={() => setViewingOffer(null)} title={viewingOffer?.name ?? '開團商品'}>
        {viewingOffer ? (
          <div className="flex flex-col gap-4">
            <Field label="售價" htmlFor="offer-view-price" hint="售價於加入開團商品時定死，契約沒有提供修改端點——這裡一律唯讀。">
              <Input id="offer-view-price" value={formatMoney(viewingOffer.sellingPrice)} readOnly />
            </Field>
            <Field label="目標採購價" htmlFor="offer-view-target">
              <Input
                id="offer-view-target"
                value={viewingOffer.targetPurchasePrice ? formatMoney(viewingOffer.targetPurchasePrice) : '—'}
                readOnly
              />
            </Field>
            <Field label="已下單數量" htmlFor="offer-view-qty">
              <Input id="offer-view-qty" value={String(viewingOffer.orderedQuantity)} readOnly />
            </Field>
            <Field label="狀態" htmlFor="offer-view-active">
              <Input id="offer-view-active" value={viewingOffer.isActive ? '啟用中' : '已停用'} readOnly />
            </Field>
          </div>
        ) : null}
      </Drawer>

      <Dialog
        open={removeTarget !== null}
        onClose={() => {
          setRemoveTarget(null);
          setRemoveError(null);
        }}
        title="確定要移除這個開團商品嗎？"
        description={removeTarget ? `「${removeTarget.name}」如果已經有訂單引用會被後端擋下。` : ''}
        footer={
          <>
            <Button variant="secondary" onClick={() => setRemoveTarget(null)} disabled={removing}>
              取消
            </Button>
            <Button variant="danger" onClick={() => void handleRemoveConfirm()} disabled={removing}>
              {removing ? '移除中…' : '確定移除'}
            </Button>
          </>
        }
      >
        {removeError ? (
          <p role="alert" className="text-sm font-medium text-danger">
            {removeError}
          </p>
        ) : null}
      </Dialog>
    </div>
  );
}
