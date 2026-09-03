'use client';

import { useMemo, useState } from 'react';
import { BottomActionBar, Button, FavoriteHeart, QuantityStepper, Toast } from '@greygray/ui';
import { addCartLine } from '@greygray/api-client/endpoints/storefront';
import { formatMoney, ApiError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';
import { publishCart } from '../../../../_lib/cartCountStore';
import { BottomBarSummary } from './BottomBarSummary';
import { UnitPriceBlock } from './UnitPriceBlock';

type S = components['schemas'];

type SubmitState = 'idle' | 'loading' | 'success' | 'error';

export interface AddToCartPanelProps {
  product: S['ProductDetail'];
}

/**
 * 商品詳情頁的互動區：選規格、選數量、加入購物車。
 *
 * **能不能買看兩件事**：
 * - `mode = Preorder` 時看 `campaign.isAcceptingOrders`（不是自己比 `closesAt`）。
 * - `mode = Stock` 時看選到的 SKU 還有沒有 `available`——
 *   預購商品的 `available` 恆為 0，那個規則不適用在這裡。
 */
export function AddToCartPanel({ product }: AddToCartPanelProps) {
  const idempotency = usePayloadIdempotency();
  const skus = product.skus;
  const hasVariants = skus.length > 1;
  const [selectedSkuId, setSelectedSkuId] = useState(skus[0]?.id ?? '');
  const [quantity, setQuantity] = useState(1);
  const [favorited, setFavorited] = useState(product.isFavorited ?? false);
  const [state, setState] = useState<SubmitState>('idle');
  const [errorMessage, setErrorMessage] = useState('');

  const selectedSku = useMemo(
    () => skus.find((sku) => sku.id === selectedSkuId) ?? skus[0],
    [skus, selectedSkuId],
  );

  if (!selectedSku) {
    return <p className="text-[length:var(--gg-text-sm)] text-fg-muted">這個商品目前沒有可購買的規格。</p>;
  }

  const price = selectedSku.price;
  const hasPrice = price != null;
  const isPreorder = product.mode === 'Preorder';
  // 預購讀 campaign.isAcceptingOrders；沒有附上 campaign 資料時保守視為不可下單。
  const isAcceptingOrders = isPreorder ? (product.campaign?.isAcceptingOrders ?? false) : true;
  const isSoldOut = !isPreorder && selectedSku.available <= 0;
  const disabled = !hasPrice || (isPreorder ? !isAcceptingOrders : isSoldOut);

  // 沒有價格時說明只出現在上方的 `UnitPriceBlock`（按鈕照樣 disabled）——
  // 兩處各說一次，畫面上就會冒出兩種說法，預購尤其明顯。
  const noPriceMessage = isPreorder
    ? '售價在開團時決定，開團後才能加入購物車。'
    : '這個規格尚未定價，請稍後再試。';

  const disabledReason = !hasPrice
    ? null
    : isPreorder && !isAcceptingOrders
      ? '這個團已經截止收單，無法加入購物車。'
      : isSoldOut
        ? '已售完，補貨後再回來看看。'
        : null;

  // 「／32 顆」這個包裝數量是 SKU 才有的資料，不是憑空算出來的金額。
  const unitPriceLabel =
    price && selectedSku.unitOfMeasure && selectedSku.unitCount
      ? `${formatMoney(price)}／${selectedSku.unitCount} ${selectedSku.unitOfMeasure}`
      : null;

  const maxQuantity = !isPreorder && hasPrice ? Math.max(selectedSku.available, 1) : 99;

  async function handleAdd() {
    if (!selectedSku || disabled) return;
    setState('loading');
    try {
      const body = {
        skuId: selectedSku.id,
        mode: product.mode,
        campaignOfferId: selectedSku.campaignOfferId ?? null,
        quantity,
      };
      const updated = await addCartLine(browserApi(), body, { idempotencyKey: idempotency.current(body) });
      idempotency.complete();
      // 端點回的就是更新後的整張購物車，直接推給分頁列的徽章——不必等重新整理，也不用自己 +1。
      publishCart(updated);
      setState('success');
    } catch (cause) {
      setState('error');
      setErrorMessage(cause instanceof ApiError ? cause.problem.title : '加入購物車失敗，請稍後再試。');
    }
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-4)]">
      {/* 單價放這裡而不是 page.tsx 的 SSR header：那一層不知道使用者選了哪個規格。 */}
      <UnitPriceBlock
        price={price}
        unitPriceLabel={unitPriceLabel}
        variantLabel={hasVariants ? (selectedSku.variantName ?? selectedSku.name) : null}
        noPriceMessage={noPriceMessage}
      />

      {hasVariants && (
        <div className="flex flex-col gap-[var(--gg-space-2)]">
          <span className="text-[length:var(--gg-text-sm)] font-bold text-fg">選擇規格</span>
          <div className="flex flex-wrap gap-[var(--gg-space-2)]">
            {skus.map((sku) => (
              <button
                key={sku.id}
                type="button"
                onClick={() => setSelectedSkuId(sku.id)}
                aria-pressed={sku.id === selectedSku.id}
                className={`rounded-pill border px-[var(--gg-space-4)] py-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] font-bold transition-colors duration-[var(--gg-duration-base)] ease-out-soft ${
                  sku.id === selectedSku.id
                    ? 'border-primary bg-primary-subtle text-primary-text'
                    : 'border-border-soft bg-surface text-fg-muted hover:bg-surface-sunken'
                }`}
              >
                {sku.variantName ?? sku.name}
              </button>
            ))}
          </div>
        </div>
      )}

      <div className="flex items-center gap-[var(--gg-space-3)]">
        <FavoriteHeart
          pressed={favorited}
          onToggle={() => setFavorited((value) => !value)}
          aria-label={favorited ? `取消收藏 ${product.name}` : `加入收藏 ${product.name}`}
        />
        <QuantityStepper
          value={quantity}
          min={1}
          max={maxQuantity}
          onChange={setQuantity}
          disabled={disabled}
          aria-label="購買數量"
        />
      </div>

      {disabledReason && <p className="text-[length:var(--gg-text-sm)] text-danger">{disabledReason}</p>}

      <BottomActionBar>
        <div className="flex flex-1 items-center justify-between gap-[var(--gg-space-3)]">
          <BottomBarSummary quantity={quantity} price={price} unitPriceLabel={unitPriceLabel} />
          <Button onClick={handleAdd} disabled={disabled} loading={state === 'loading'}>
            加入購物車
          </Button>
        </div>
      </BottomActionBar>

      <div className="fixed inset-x-[var(--gg-space-4)] bottom-[calc(var(--gg-bottom-bar-height)+var(--gg-space-3))] z-[var(--gg-z-modal)] mx-auto max-w-sm">
        {/*
          #32：提示裡要有一條真的走得到購物車的路。5 秒不是隨手挑的——
          預設的 2.5 秒是「看一眼」的長度，按不到的按鈕比沒有按鈕更糟。
        */}
        <Toast
          open={state === 'success'}
          variant="success"
          message="已加入購物車。"
          onClose={() => setState('idle')}
          duration={5000}
          action={{ label: '查看購物車', href: '/cart' }}
        />
        <Toast open={state === 'error'} variant="error" message={errorMessage} onClose={() => setState('idle')} duration={4000} />
      </div>
    </div>
  );
}
