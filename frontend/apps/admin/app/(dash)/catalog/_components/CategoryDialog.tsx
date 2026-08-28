'use client';

import { createCategory, updateCategory } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Input, useToast } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface CategoryDialogProps {
  readonly open: boolean;
  readonly category: S['Category'] | null;
  readonly onClose: () => void;
  readonly onSaved: () => void;
}

/** 新增／編輯共用同一個 Dialog——`category` 為 `null` 代表新增。 */
export function CategoryDialog({ open, category, onClose, onSaved }: CategoryDialogProps) {
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const [name, setName] = useState('');
  const [imageUrl, setImageUrl] = useState('');
  const [sortOrder, setSortOrder] = useState('0');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setName(category?.name ?? '');
    setImageUrl(category?.imageUrl ?? '');
    setSortOrder(String(category?.sortOrder ?? 0));
    setError(null);
  }, [open, category]);

  async function handleSubmit() {
    if (!name.trim()) return;
    setSubmitting(true);
    setError(null);
    try {
      const client = browserApi();
      const body: S['CategoryInput'] = {
        name: name.trim(),
        imageUrl: imageUrl.trim() ? imageUrl.trim() : null,
        sortOrder: Number.parseInt(sortOrder, 10) || 0,
      };
      const payload = { kind: category ? 'update' : 'create', categoryId: category?.id ?? null, body };
      const options = { idempotencyKey: idempotency.current(payload) };
      if (category) {
        await updateCategory(client, category.id, body, options);
      } else {
        await createCategory(client, body, options);
      }
      idempotency.complete();
      toast.show('success', category ? '分類已更新。' : '分類已新增。');
      onSaved();
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
      onClose={onClose}
      title={category ? '編輯分類' : '新增分類'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting || !name.trim()}>
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
        <Field label="分類名稱" htmlFor="category-name" required>
          <Input
            id="category-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            maxLength={50}
          />
        </Field>
        <Field label="圖片網址" htmlFor="category-image" hint="選填，橫捲分類標用">
          <Input id="category-image" value={imageUrl} onChange={(event) => setImageUrl(event.target.value)} />
        </Field>
        <Field label="排序" htmlFor="category-sort" hint="數字越小排越前面">
          <Input
            id="category-sort"
            type="number"
            value={sortOrder}
            onChange={(event) => setSortOrder(event.target.value)}
          />
        </Field>
      </div>
    </Dialog>
  );
}
