'use client';

import { createProduct, listCategories } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';
import { ProductForm } from '../../_components/ProductForm';

type S = components['schemas'];

export default function NewProductPage() {
  const router = useRouter();
  const idempotency = usePayloadIdempotency();
  const [categories, setCategories] = useState<readonly S['Category'][]>([]);

  useEffect(() => {
    let cancelled = false;
    void listCategories(browserApi()).then(
      (items) => {
        if (!cancelled) setCategories(items);
      },
      () => {
        // 沒有分類也能建立商品（categoryId 可以是 null），拿不到清單就先讓下拉是空的。
      },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleSubmit(body: S['AdminProductInput']) {
    const created = await createProduct(browserApi(), body, { idempotencyKey: idempotency.current(body) });
    idempotency.complete();
    router.push(`/catalog/products/${created.id}`);
  }

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-fg">新增商品</h1>
      <p className="max-w-xl text-sm text-fg-muted">
        建立後才能新增 SKU。契約目前只有「修改 SKU」端點，沒有「新增 SKU」端點——見交付回報的契約問題。
      </p>
      <ProductForm categories={categories} submitLabel="建立商品" onSubmit={handleSubmit} />
    </div>
  );
}
