'use client';

import { useState } from 'react';
import { Button, PriceDisplay, QuantityStepper, Toast, Thumbnail } from '@greygray/ui';
import { addCartLine } from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { ApiError } from '@greygray/api-client';
import { browserApi } from '../../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../../_lib/usePayloadIdempotency';

type S = components['schemas'];

type SubmitState = 'idle' | 'loading' | 'success' | 'error';

export interface CampaignOfferRowProps {
  offer: S['CampaignOffer'];
  /** 讀 `CampaignDetail.isAcceptingOrders`，不要自己拿 `closesAt` 跟現在時間比。 */
  isAcceptingOrders: boolean;
}

/** 開團詳情頁單一品項：數量 ＋ 加入購物車。截團或品項下架時鈕會 disabled 並說明原因。 */
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
      await addCartLine(browserApi(), body, { idempotencyKey: idempotency.current(body) });
      idempotency.complete();
      setState('success');
    } catch (cause) {
      setState('error');
      setErrorMessage(cause instanceof ApiError ? cause.problem.title : '加入購物車失敗，請稍後再試。');
    }
  }

  return (
    <div className="flex items-center gap-[var(--gg-space-3)] rounded-card bg-surface p-[var(--gg-space-3)] shadow-card">
      <div className="h-[var(--gg-space-8)] w-[var(--gg-space-8)] shrink-0 overflow-hidden rounded-card bg-surface-sunken">
        <Thumbnail src={offer.imageUrl} alt={offer.name} sizes="64px" />
      </div>

      <div className="flex flex-1 flex-col gap-[var(--gg-space-1)]">
        <p className="line-clamp-1 font-display font-bold text-fg">{offer.name}</p>
        {offer.variantName && (
          <p className="text-[length:var(--gg-text-xs)] text-fg-muted">{offer.variantName}</p>
        )}
        <PriceDisplay amount={offer.sellingPrice} size="sm" />
        {offer.unitPriceLabel && (
          <span className="text-[length:var(--gg-text-xs)] text-fg-muted">{offer.unitPriceLabel}</span>
        )}
      </div>

      <div className="flex flex-col items-end gap-[var(--gg-space-2)]">
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
          <span className="max-w-32 text-right text-[length:var(--gg-text-xs)] text-danger">
            {disabledReason}
          </span>
        )}
      </div>

      <div className="fixed inset-x-[var(--gg-space-4)] bottom-[var(--gg-space-4)] z-[var(--gg-z-modal)] mx-auto max-w-sm">
        <Toast open={state === 'success'} variant="success" message="已加入購物車。" onClose={() => setState('idle')} duration={2500} />
        <Toast open={state === 'error'} variant="error" message={errorMessage} onClose={() => setState('idle')} duration={4000} />
      </div>
    </div>
  );
}
