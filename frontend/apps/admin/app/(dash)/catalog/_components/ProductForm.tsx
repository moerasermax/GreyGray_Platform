'use client';

import type { components } from '@greygray/api-client/admin';
import { Field, Input, Select, Textarea } from '@greygray/ui/admin';
import { useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface ProductFormProps {
  readonly initial?: S['AdminProductInput'];
  readonly categories: readonly S['Category'][];
  readonly submitLabel: string;
  readonly onSubmit: (body: S['AdminProductInput']) => Promise<void>;
}

const MODE_OPTIONS: readonly { value: S['FulfillmentMode']; label: string }[] = [
  { value: 'Stock', label: '現貨（Stock）' },
  { value: 'Preorder', label: '預購（Preorder）' },
];

/** 商品層級的欄位（`AdminProductInput`）。SKU 層級的重量／尺寸不在這裡——見 `SkuEditDrawer`。 */
export function ProductForm({ initial, categories, submitLabel, onSubmit }: ProductFormProps) {
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [shortDescription, setShortDescription] = useState(initial?.shortDescription ?? '');
  const [categoryId, setCategoryId] = useState(initial?.categoryId ?? '');
  const [mode, setMode] = useState<S['FulfillmentMode']>(initial?.mode ?? 'Stock');
  const [imageUrl, setImageUrl] = useState(initial?.images?.[0] ?? '');
  const [isActive, setIsActive] = useState(initial?.isActive ?? true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit() {
    if (!name.trim()) return;
    setSubmitting(true);
    setError(null);
    try {
      const body: S['AdminProductInput'] = {
        name: name.trim(),
        description: description.trim() ? description.trim() : null,
        shortDescription: shortDescription.trim() ? shortDescription.trim() : null,
        categoryId: categoryId || null,
        mode,
        images: imageUrl.trim() ? [imageUrl.trim()] : [],
        isActive,
      };
      await onSubmit(body);
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex max-w-xl flex-col gap-4 rounded-card border border-border-soft bg-surface p-5 shadow-card">
      {error ? (
        <p role="alert" className="text-sm font-medium text-danger">
          {error}
        </p>
      ) : null}

      <Field label="商品名稱" htmlFor="product-name" required>
        <Input id="product-name" value={name} onChange={(event) => setName(event.target.value)} maxLength={100} />
      </Field>

      <Field label="短描述" htmlFor="product-short-description" hint="商品卡牆用，最多 100 字">
        <Input
          id="product-short-description"
          value={shortDescription ?? ''}
          onChange={(event) => setShortDescription(event.target.value)}
          maxLength={100}
        />
      </Field>

      <Field label="完整描述" htmlFor="product-description">
        <Textarea
          id="product-description"
          rows={4}
          value={description ?? ''}
          onChange={(event) => setDescription(event.target.value)}
        />
      </Field>

      <Field label="分類" htmlFor="product-category">
        <Select
          id="product-category"
          placeholder="未分類"
          value={categoryId ?? ''}
          onChange={(event) => setCategoryId(event.target.value)}
          options={categories.map((category) => ({ value: category.id, label: category.name }))}
        />
      </Field>

      <Field label="銷售模式" htmlFor="product-mode" required hint="預購商品可用量恆為 0，但仍然可以下單，售價在開團時才定">
        <Select
          id="product-mode"
          value={mode}
          onChange={(event) => setMode(event.target.value as S['FulfillmentMode'])}
          options={MODE_OPTIONS}
        />
      </Field>

      <Field label="封面圖片網址" htmlFor="product-image" hint="選填，商品卡牆用 1:1 裁切">
        <Input id="product-image" value={imageUrl} onChange={(event) => setImageUrl(event.target.value)} />
      </Field>

      <label className="flex items-center gap-2 text-sm text-fg">
        <input
          type="checkbox"
          checked={isActive}
          onChange={(event) => setIsActive(event.target.checked)}
          className="h-4 w-4 rounded-sm border-border-strong"
        />
        上架中（取消勾選會從前台下架，但不影響既有訂單）
      </label>

      <div className="flex justify-end">
        <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting || !name.trim()}>
          {submitting ? '儲存中…' : submitLabel}
        </Button>
      </div>
    </div>
  );
}
