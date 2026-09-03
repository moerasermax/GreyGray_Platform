'use client';

import { ApiError } from '@greygray/api-client';
import { getProduct, listCategories, updateProduct } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { ErrorState, useToast } from '@greygray/ui/admin';
import { useParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../../login/_lib/session';
import { LotDrawer } from '../../_components/LotDrawer';
import { ProductForm } from '../../_components/ProductForm';
import { SkuEditDrawer } from '../../_components/SkuEditDrawer';
import { SkuSection } from '../../_components/SkuSection';

type S = components['schemas'];

/** SKU 抽屜的兩種狀態：新增（沒有既有 SKU）與編輯（有）。關著的時候是 `null`。 */
type SkuDrawerState = { readonly sku: S['AdminSku'] | null } | null;

export default function ProductDetailPage() {
  const params = useParams<{ productId: string }>();
  const productId = params.productId;
  const idempotency = usePayloadIdempotency();
  const toast = useToast();

  const [product, setProduct] = useState<S['AdminProduct'] | null>(null);
  const [categories, setCategories] = useState<readonly S['Category'][]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [skuDrawer, setSkuDrawer] = useState<SkuDrawerState>(null);
  // 只記 id，不記整個 SKU 物件：進貨成功後商品會重新 GET，抽屜上的「目前可用量」
  // 要跟著變成新的那一份，記著舊物件的話它會停在進貨前的數字。
  const [receivingSkuId, setReceivingSkuId] = useState<string | null>(null);

  const receivingSku = product?.skus.find((sku) => sku.id === receivingSkuId) ?? null;
  const canWrite = hasRequiredRole(getSession()?.role ?? 'ReadOnly', 'Operator');

  useEffect(() => {
    let cancelled = false;
    void listCategories(browserApi()).then(
      (items) => {
        if (!cancelled) setCategories(items);
      },
      () => {},
    );
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void getProduct(browserApi(), productId)
      .then((found) => {
        if (!cancelled) setProduct(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取商品失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [productId, reloadKey]);

  async function handleSubmit(body: S['AdminProductInput']) {
    const payload = { productId, body };
    await updateProduct(browserApi(), productId, body, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    // 寫入完成後重新 GET，不要拿 request 內容當成新狀態（docs/05 §9）。
    setReloadKey((current) => current + 1);
  }

  function reload() {
    setReloadKey((current) => current + 1);
  }

  if (error) {
    return (
      <ErrorState
        title={error instanceof ApiError ? error.problem.title : error.message}
        traceId={error instanceof ApiError ? error.shortTraceId : null}
        onRetry={() => {
          setError(null);
          reload();
        }}
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-semibold text-fg">{loading ? '載入中…' : product?.name}</h1>

      {product ? (
        <ProductForm
          key={reloadKey}
          initial={product}
          categories={categories}
          submitLabel="儲存商品資料"
          onSubmit={handleSubmit}
        />
      ) : null}

      <SkuSection
        mode={product?.mode ?? 'Stock'}
        skus={product?.skus ?? []}
        loading={loading}
        canWrite={canWrite}
        onAdd={() => setSkuDrawer({ sku: null })}
        onEdit={(sku) => setSkuDrawer({ sku })}
        onReceive={(sku) => setReceivingSkuId(sku.id)}
      />

      <SkuEditDrawer
        open={skuDrawer !== null}
        sku={skuDrawer?.sku ?? null}
        productId={productId}
        fulfillmentMode={product?.mode ?? 'Stock'}
        onClose={() => setSkuDrawer(null)}
        onSaved={() => {
          toast.show('success', skuDrawer?.sku ? 'SKU 已更新。' : 'SKU 已建立。');
          reload();
        }}
      />

      <LotDrawer
        open={receivingSkuId !== null}
        sku={receivingSku}
        onClose={() => setReceivingSkuId(null)}
        onReceived={() => {
          toast.show('success', '已進貨，可用量已更新。');
          reload();
        }}
      />
    </div>
  );
}
