'use client';

import { useState } from 'react';
import { Button, PriceDisplay, QuantityStepper, Toast, Thumbnail } from '@greygray/ui';
import { addCartLine } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { ApiError } from '@greygray/api-client';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';
import { publishCart } from '../../../../_lib/cartCountStore';

type S = components['schemas'];

type SubmitState = 'idle' | 'loading' | 'success' | 'error';

export interface CampaignOfferRowProps {
  offer: S['CampaignOffer'];
  /** 讀 `CampaignDetail.isAcceptingOrders`，不要自己拿 `closesAt` 跟現在時間比。 */
  isAcceptingOrders: boolean;
}

/**
 * 開團詳情頁單一品項：數量 ＋ 加入購物車。截團或品項下架時鈕會 disabled 並說明原因。
 *
 * 版面跟購物車列同一個做法：兩欄 grid（縮圖｜文字），數量、加購與禁用原因在手機上
 * **橫跨兩欄放下一行**——390px 下三者同列時，品名被數量控制擠到只剩「韓國雪花…」。
 * `sm` 以上控制區才回到同一列的第三欄。文字欄 `minmax(0,1fr)` ＋ `min-w-0`，長品名與規格折行而不是撐爆。
 */
export function CampaignOfferRow({ offer, isAcceptingOrders }: CampaignOfferRowProps) {
  const idempotency = usePayloadIdempotency();
  const [quantity, setQuantity] = useState(1);
  const [state, setState] = useState<SubmitState>('idle');
  const [errorMessage, setErrorMessage] = useState('');

  const disabled = !isAcceptingOrders || !offer.isActive;
  const disabledReason = !offer.isActive ? '這個品項目前已下架' : !isAcceptingOrders ? '這個團已截止收單' : null;

  async function handleAdd() {
    setState('loading');
    try {
      const body = {
        skuId: offer.skuId,
        mode: 'Preorder',
        campaignOfferId: offer.id,
        quantity,
      } as const;
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
    <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-[var(--gg-space-3)] gap-y-[var(--gg-space-3)] rounded-card bg-surface p-[var(--gg-space-3)] shadow-card sm:grid-cols-[auto_minmax(0,1fr)_auto] sm:items-center">
      <div className="relative flex h-[var(--gg-space-8)] w-[var(--gg-space-8)] shrink-0 items-center justify-center overflow-hidden rounded-card border border-border-soft bg-surface-sunken">
        {/* 沒圖時 `Thumbnail` 什麼都不畫；補一個中性標示，不捏造商品圖。品名就在旁邊，所以對輔助科技隱藏。 */}
        {!offer.imageUrl && (
          <span aria-hidden className="text-[length:var(--gg-text-xs)] text-fg-muted">
            無圖
          </span>
        )}
        <Thumbnail src={offer.imageUrl} alt={offer.name} sizes="64px" />
      </div>

      <div className="flex min-w-0 flex-col gap-[var(--gg-space-1)]">
        <p className="break-words font-display font-bold text-fg">{offer.name}</p>
        {offer.variantName && (
          <p className="break-words text-[length:var(--gg-text-xs)] text-fg-muted">{offer.variantName}</p>
        )}
        <div className="flex flex-wrap items-baseline gap-x-[var(--gg-space-2)] gap-y-[var(--gg-space-1)]">
          <PriceDisplay amount={offer.sellingPrice} size="sm" />
          {offer.unitPriceLabel && (
            <span className="min-w-0 break-words text-[length:var(--gg-text-xs)] text-fg-muted">{offer.unitPriceLabel}</span>
          )}
        </div>
      </div>

      <div className="col-span-2 flex flex-wrap items-center justify-between gap-x-[var(--gg-space-3)] gap-y-[var(--gg-space-2)] border-t border-border-soft pt-[var(--gg-space-3)] sm:col-span-1 sm:flex-col sm:flex-nowrap sm:items-end sm:justify-center sm:border-t-0 sm:pt-0">
        <QuantityStepper
          value={quantity}
          min={1}
          max={99}
          onChange={setQuantity}
          disabled={disabled}
          aria-label={`${offer.name} 數量`}
        />
        <Button size="sm" onClick={handleAdd} disabled={disabled} loading={state === 'loading'}>
          加入購物車
        </Button>
        {disabled && disabledReason && (
          <span className="basis-full text-[length:var(--gg-text-xs)] text-danger sm:max-w-32 sm:basis-auto sm:text-right">
            {disabledReason}
          </span>
        )}
      </div>

      {/* 這一頁有底部分頁列：提示抬到分頁列之上，用的是 `globals.css` 替 body 留白的同一個算式，不猜固定高度。 */}
      <div className="fixed inset-x-[var(--gg-space-4)] bottom-[calc(var(--gg-bottom-bar-height)+env(safe-area-inset-bottom,0px)+var(--gg-space-4))] z-[var(--gg-z-modal)] mx-auto max-w-sm">
        {/* #32：同商品頁，成功提示裡給一條走得到購物車的路；有動作就要留得夠久（5 秒）。 */}
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
