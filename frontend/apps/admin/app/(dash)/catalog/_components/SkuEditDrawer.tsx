'use client';

import { createSku, updateSku } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { Drawer, Field, Input } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { apiErrorMessage, apiFieldErrors, fieldErrorText } from '../_lib/apiError';
import { EMPTY_SKU_FORM, buildSkuInput, skuFormFromSku, type SkuFormFields } from '../_lib/skuForm';
import { Button } from './Button';

type S = components['schemas'];

export interface SkuEditDrawerProps {
  readonly open: boolean;
  /** `null` ＝ **新增模式**（`POST /v1/products/{productId}/skus`，ADR-032）。 */
  readonly sku: S['AdminSku'] | null;
  /** 新增模式要打的商品。編輯模式用不到，但一併傳進來比較不會忘。 */
  readonly productId: string;
  /** 商品的出貨模式：只有 `Stock` 才問現貨標價，預購的售價在開團時才定。 */
  readonly fulfillmentMode: S['FulfillmentMode'];
  readonly onClose: () => void;
  readonly onSaved: () => void;
}

/**
 * 新增（`POST /v1/products/{productId}/skus`）與編輯（`PATCH /v1/skus/{skuId}`）共用同一份表單。
 *
 * ADR-032 之前契約只有 PATCH，所以這個 Drawer 只能編輯既有 SKU；BE-44 補上新增端點之後
 * 兩種模式只差在「打哪一支」，欄位規則完全一樣——所以規則放在 `_lib/skuForm.ts` 一份，
 * 兩邊都走 `buildSkuInput`。
 *
 * `weightGram` 與 `size` 在這裡設成必填：M1a 運費一口價用不到，
 * 但 M3 啟用材積重計費時資料已經在那裡，回頭補幾百筆商品尺寸是純粹的浪費。
 */
export function SkuEditDrawer({ open, sku, productId, fulfillmentMode, onClose, onSaved }: SkuEditDrawerProps) {
  const idempotency = usePayloadIdempotency();
  const isCreate = sku === null;
  const [fields, setFields] = useState<SkuFormFields>(EMPTY_SKU_FORM);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  // 預購商品不問現貨標價；但既有 SKU 已經填過的話還是要看得到，
  // 不然「隱藏欄位」等於偷偷把它清成 null。
  const showListPrice = fulfillmentMode === 'Stock' || sku?.listPrice != null;

  useEffect(() => {
    if (!open) return;
    setFields(sku ? skuFormFromSku(sku) : EMPTY_SKU_FORM);
    setError(null);
    setFieldErrors({});
  }, [open, sku]);

  function patch(changes: Partial<SkuFormFields>) {
    setFields((current) => ({ ...current, ...changes }));
  }

  async function handleSubmit() {
    const built = buildSkuInput(fields);
    if (!built.ok) {
      setError(built.message);
      return;
    }
    setSubmitting(true);
    setError(null);
    setFieldErrors({});
    try {
      const body = built.body;
      if (isCreate) {
        const payload = { productId, body };
        await createSku(browserApi(), productId, body, { idempotencyKey: idempotency.current(payload) });
      } else {
        const payload = { skuId: sku.id, body };
        await updateSku(browserApi(), sku.id, body, { idempotencyKey: idempotency.current(payload) });
      }
      idempotency.complete();
      onSaved();
      onClose();
    } catch (cause) {
      setError(apiErrorMessage(cause));
      setFieldErrors(apiFieldErrors(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={sku ? `編輯 SKU：${sku.name}` : '新增 SKU'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? (isCreate ? '建立中…' : '儲存中…') : isCreate ? '建立' : '儲存'}
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

        <Field label="SKU 名稱" htmlFor="sku-name" required error={fieldErrorText(fieldErrors, 'name')}>
          <Input id="sku-name" value={fields.name} onChange={(event) => patch({ name: event.target.value })} maxLength={100} />
        </Field>

        <Field
          label="規格／款式"
          htmlFor="sku-variant"
          hint="例如「30 入」「粉色」，沒有變體留空"
          error={fieldErrorText(fieldErrors, 'variantName')}
        >
          <Input
            id="sku-variant"
            value={fields.variantName}
            onChange={(event) => patch({ variantName: event.target.value })}
            maxLength={50}
          />
        </Field>

        <Field
          label="重量（公克）"
          htmlFor="sku-weight"
          required
          hint="M1a 運費一口價用不到，但 M3 材積重計費要用，現在填起來之後不用補幾百筆舊資料。"
          error={fieldErrorText(fieldErrors, 'weightGram')}
        >
          <Input
            id="sku-weight"
            type="number"
            min={0}
            value={fields.weightGram}
            onChange={(event) => patch({ weightGram: event.target.value })}
          />
        </Field>

        <Field label="尺寸（公分，長／寬／高）" htmlFor="sku-length" required error={fieldErrorText(fieldErrors, 'size')}>
          <div className="grid grid-cols-3 gap-2">
            <Input
              id="sku-length"
              type="number"
              min={0}
              placeholder="長"
              value={fields.lengthCm}
              onChange={(event) => patch({ lengthCm: event.target.value })}
            />
            <Input
              type="number"
              min={0}
              placeholder="寬"
              value={fields.widthCm}
              onChange={(event) => patch({ widthCm: event.target.value })}
            />
            <Input
              type="number"
              min={0}
              placeholder="高"
              value={fields.heightCm}
              onChange={(event) => patch({ heightCm: event.target.value })}
            />
          </div>
        </Field>

        <Field label="計量單位" htmlFor="sku-uom" hint="選填，例如「盒」「瓶」" error={fieldErrorText(fieldErrors, 'unitOfMeasure')}>
          <Input id="sku-uom" value={fields.unitOfMeasure} onChange={(event) => patch({ unitOfMeasure: event.target.value })} />
        </Field>

        <Field label="每單位入數" htmlFor="sku-unit-count" hint="選填" error={fieldErrorText(fieldErrors, 'unitCount')}>
          <Input
            id="sku-unit-count"
            type="number"
            min={0}
            value={fields.unitCount}
            onChange={(event) => patch({ unitCount: event.target.value })}
          />
        </Field>

        {showListPrice ? (
          <Field
            label="現貨標價（元）"
            htmlFor="sku-list-price"
            hint="預購 SKU 不用填，售價在開團時才定"
            error={fieldErrorText(fieldErrors, 'listPrice')}
          >
            <Input
              id="sku-list-price"
              type="number"
              min={0}
              value={fields.listPriceMajor}
              onChange={(event) => patch({ listPriceMajor: event.target.value })}
            />
          </Field>
        ) : null}

        <label className="flex items-center gap-2 text-sm text-fg">
          <input
            type="checkbox"
            checked={fields.isActive}
            onChange={(event) => patch({ isActive: event.target.checked })}
            className="h-4 w-4 rounded-sm border-border-strong"
          />
          啟用中
        </label>
      </div>
    </Drawer>
  );
}
