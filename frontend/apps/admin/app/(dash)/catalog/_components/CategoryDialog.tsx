'use client';

import { createCategory, updateCategory } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Input, Select, useToast } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];
type Category = S['Category'];

export const CATEGORY_PARENT_HINT = '這個分類底下已有子分類，不能再設為子分類';

export function orderCategories(categories: readonly Category[]): Category[] {
  const byId = new Map(categories.map((category) => [category.id, category]));
  const isDirectChild = (category: Category) => {
    if (!category.parentId) return false;
    const parent = byId.get(category.parentId);
    return Boolean(parent && !parent.parentId);
  };
  return categories.flatMap((category) => {
    if (isDirectChild(category)) return [];
    return [
      category,
      ...categories.filter((candidate) => candidate.parentId === category.id && isDirectChild(candidate)),
    ];
  });
}

export function categoryOptions(categories: readonly Category[]) {
  const byId = new Map(categories.map((category) => [category.id, category]));
  return orderCategories(categories).map((category) => {
    const parent = category.parentId ? byId.get(category.parentId) : null;
    return {
      value: category.id,
      label: parent && !parent.parentId ? `${parent.name} › ${category.name}` : category.name,
    };
  });
}

export function categoryParentField(categories: readonly Category[], categoryId: string | null) {
  const hasChildren = categoryId !== null && categories.some((category) => category.parentId === categoryId);
  return {
    disabled: hasChildren,
    hint: hasChildren ? CATEGORY_PARENT_HINT : null,
    options: categories
      .filter((category) => !category.parentId && category.id !== categoryId)
      .map((category) => ({ value: category.id, label: category.name })),
  };
}

export function buildCategoryInput(values: {
  readonly name: string;
  readonly imageUrl: string;
  readonly sortOrder: string;
  readonly parentId: string;
}): S['CategoryInput'] {
  return {
    name: values.name.trim(),
    imageUrl: values.imageUrl.trim() ? values.imageUrl.trim() : null,
    sortOrder: Number.parseInt(values.sortOrder, 10) || 0,
    parentId: values.parentId || null,
  };
}

export interface CategoryDialogProps {
  readonly open: boolean;
  readonly category: S['Category'] | null;
  readonly categories: readonly S['Category'][];
  readonly onClose: () => void;
  readonly onSaved: () => void;
}

/** 新增／編輯共用同一個 Dialog——`category` 為 `null` 代表新增。 */
export function CategoryDialog({ open, category, categories, onClose, onSaved }: CategoryDialogProps) {
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const [name, setName] = useState('');
  const [imageUrl, setImageUrl] = useState('');
  const [sortOrder, setSortOrder] = useState('0');
  const [parentId, setParentId] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setName(category?.name ?? '');
    setImageUrl(category?.imageUrl ?? '');
    setSortOrder(String(category?.sortOrder ?? 0));
    setParentId(category?.parentId ?? '');
    setError(null);
  }, [open, category]);

  async function handleSubmit() {
    if (!name.trim()) return;
    setSubmitting(true);
    setError(null);
    try {
      const client = browserApi();
      const body = buildCategoryInput({ name, imageUrl, sortOrder, parentId });
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

  const parentField = categoryParentField(categories, category?.id ?? null);

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
        <Field
          label="上層分類"
          htmlFor="category-parent"
          {...(parentField.hint ? { hint: parentField.hint } : {})}
        >
          <Select
            id="category-parent"
            placeholder="（無，作為根分類）"
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
            options={parentField.options}
            disabled={parentField.disabled}
            {...(parentField.hint ? { 'aria-describedby': 'category-parent-hint' } : {})}
          />
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
