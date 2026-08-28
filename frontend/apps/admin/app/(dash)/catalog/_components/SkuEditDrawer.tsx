'use client';

import { moneyFromMajorInput, moneyToMajorInput } from '@greygray/api-client';
import { updateSku } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { Drawer, Field, Input } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface SkuEditDrawerProps {
  readonly open: boolean;
  readonly sku: S['AdminSku'] | null;
  readonly onClose: () => void;
  readonly onSaved: () => void;
}

/**
 * `PATCH /v1/skus/{skuId}` 是契約裡**唯一**能寫入 SKU 的端點——沒有「新增 SKU」端點，
 * 所以這個 Drawer 只能編輯既有 SKU（見交付回報的契約問題）。
 *
 * `weightGram` 與 `size` 在這裡設成必填：M1a 運費一口價用不到，
 * 但 M3 啟用材積重計費時資料已經在那裡，回頭補幾百筆商品尺寸是純粹的浪費。
 */
export function SkuEditDrawer({ open, sku, onClose, onSaved }: SkuEditDrawerProps) {
  const idempotency = usePayloadIdempotency();
  const [name, setName] = useState('');
  const [variantName, setVariantName] = useState('');
  const [weightGram, setWeightGram] = useState('');
  const [lengthCm, setLengthCm] = useState('');
  const [widthCm, setWidthCm] = useState('');
  const [heightCm, setHeightCm] = useState('');
  const [unitOfMeasure, setUnitOfMeasure] = useState('');
  const [unitCount, setUnitCount] = useState('');
  const [listPriceMajor, setListPriceMajor] = useState('');
  const [isActive, setIsActive] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !sku) return;
    setName(sku.name);
    setVariantName(sku.variantName ?? '');
    setWeightGram(String(sku.weightGram));
    setLengthCm(String(sku.size.lengthCm));
    setWidthCm(String(sku.size.widthCm));
    setHeightCm(String(sku.size.heightCm));
    setUnitOfMeasure(sku.unitOfMeasure ?? '');
    setUnitCount(sku.unitCount !== null && sku.unitCount !== undefined ? String(sku.unitCount) : '');
    setListPriceMajor(sku.listPrice ? moneyToMajorInput(sku.listPrice) : '');
    setIsActive(sku.isActive);
    setError(null);
  }, [open, sku]);

  async function handleSubmit() {
    if (!sku) return;
    const weight = Number.parseInt(weightGram, 10);
    const length = Number.parseFloat(lengthCm);
    const width = Number.parseFloat(widthCm);
    const height = Number.parseFloat(heightCm);
    if (!name.trim() || !Number.isFinite(weight) || !Number.isFinite(length) || !Number.isFinite(width) || !Number.isFinite(height)) {
      setError('名稱、重量與尺寸為必填，且必須是數字。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const unitCountValue = unitCount.trim() ? Number.parseInt(unitCount, 10) : null;
      const listPriceValue = listPriceMajor.trim() ? moneyFromMajorInput(listPriceMajor, 'TWD') : null;
      if (listPriceMajor.trim() && !listPriceValue) {
        setError('現貨標價格式不正確，最多只能有兩位小數。');
        return;
      }
      const body: S['AdminSkuInput'] = {
        name: name.trim(),
        variantName: variantName.trim() ? variantName.trim() : null,
        weightGram: weight,
        size: { lengthCm: length, widthCm: width, heightCm: height },
        unitOfMeasure: unitOfMeasure.trim() ? unitOfMeasure.trim() : null,
        unitCount: unitCountValue,
        listPrice: listPriceValue,
        isActive,
      };
      const payload = { skuId: sku.id, body };
      await updateSku(browserApi(), sku.id, body, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      onSaved();
      onClose();
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={sku ? `編輯 SKU：${sku.name}` : '編輯 SKU'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '儲存中…' : '儲存'}
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

        <Field label="SKU 名稱" htmlFor="sku-name" required>
          <Input id="sku-name" value={name} onChange={(event) => setName(event.target.value)} maxLength={100} />
        </Field>

        <Field label="規格／款式" htmlFor="sku-variant" hint="例如「30 入」「粉色」，沒有變體留空">
          <Input id="sku-variant" value={variantName} onChange={(event) => setVariantName(event.target.value)} maxLength={50} />
        </Field>

        <Field
          label="重量（公克）"
          htmlFor="sku-weight"
          required
          hint="M1a 運費一口價用不到，但 M3 材積重計費要用，現在填起來之後不用補幾百筆舊資料。"
        >
          <Input id="sku-weight" type="number" min={0} value={weightGram} onChange={(event) => setWeightGram(event.target.value)} />
        </Field>

        <Field label="尺寸（公分，長／寬／高）" htmlFor="sku-length" required>
          <div className="grid grid-cols-3 gap-2">
            <Input id="sku-length" type="number" min={0} placeholder="長" value={lengthCm} onChange={(event) => setLengthCm(event.target.value)} />
            <Input type="number" min={0} placeholder="寬" value={widthCm} onChange={(event) => setWidthCm(event.target.value)} />
            <Input type="number" min={0} placeholder="高" value={heightCm} onChange={(event) => setHeightCm(event.target.value)} />
          </div>
        </Field>

        <Field label="計量單位" htmlFor="sku-uom" hint="選填，例如「盒」「瓶」">
          <Input id="sku-uom" value={unitOfMeasure} onChange={(event) => setUnitOfMeasure(event.target.value)} />
        </Field>

        <Field label="每單位入數" htmlFor="sku-unit-count" hint="選填">
          <Input id="sku-unit-count" type="number" min={0} value={unitCount} onChange={(event) => setUnitCount(event.target.value)} />
        </Field>

        <Field label="現貨標價（元）" htmlFor="sku-list-price" hint="預購 SKU 不用填，售價在開團時才定">
          <Input id="sku-list-price" type="number" min={0} value={listPriceMajor} onChange={(event) => setListPriceMajor(event.target.value)} />
        </Field>

        <label className="flex items-center gap-2 text-sm text-fg">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(event) => setIsActive(event.target.checked)}
            className="h-4 w-4 rounded-sm border-border-strong"
          />
          啟用中
        </label>
      </div>
    </Drawer>
  );
}
