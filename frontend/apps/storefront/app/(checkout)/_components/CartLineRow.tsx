'use client';

import { Badge, IconAlertTriangle, IconButton, IconX, PriceDisplay, QuantityStepper, Thumbnail } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface CartLineRowProps {
  line: S['CartLine'];
  onQuantityChange: (quantity: number) => void;
  onRemove: () => void;
  busy?: boolean;
}

/**
 * 縮圖走 `packages/ui` 的 `Thumbnail`（`next/image` ＋ 沒圖就不發請求），
 * 跟商品卡是同一套，不在這個路徑自己重新發明一次。
 *
 * 版面是兩欄 grid：縮圖｜文字。數量與小計那一列在手機上**橫跨兩欄**——
 * 390px 下塞在文字欄裡時，數量控制與金額擠在同一列、金額貼住卡片右緣。
 * 文字欄用 `minmax(0,1fr)` ＋ `min-w-0`，長商品名與規格才折得了行而不是把列撐爆。
 */
export function CartLineRow({ line, onQuantityChange, onRemove, busy = false }: CartLineRowProps) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-3)] border-b border-border-soft py-[var(--gg-space-4)] first:pt-0 last:border-b-0 last:pb-0">
      <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-[var(--gg-space-3)] gap-y-[var(--gg-space-3)]">
        <span className="relative flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface-sunken">
          {/* 沒圖時 `Thumbnail` 什麼都不畫；這裡補一個中性標示，不捏造商品圖。名稱就在旁邊，所以對輔助科技隱藏。 */}
          {!line.imageUrl && (
            <span aria-hidden className="text-[length:var(--gg-text-xs)] text-fg-muted">
              無圖
            </span>
          )}
          <Thumbnail src={line.imageUrl} alt="" sizes="64px" />
        </span>

        <div className="flex min-w-0 flex-col gap-[var(--gg-space-2)]">
          <div className="flex items-start justify-between gap-[var(--gg-space-2)]">
            <div className="min-w-0 flex-1">
              <p className="break-words font-display text-[length:var(--gg-text-base)] font-bold text-fg">{line.name}</p>
              {line.variantName && (
                <p className="break-words text-[length:var(--gg-text-sm)] text-fg-muted">{line.variantName}</p>
              )}
            </div>
            <IconButton
              icon={<IconX />}
              aria-label="移除這個品項"
              size="sm"
              onClick={onRemove}
              disabled={busy}
              className="shrink-0"
            />
          </div>

          <div className="flex flex-wrap items-center gap-[var(--gg-space-2)]">
            <Badge variant={line.mode} label={line.mode === 'Stock' ? '現貨' : '預購'} />
            <PriceDisplay amount={line.unitPrice} size="sm" />
          </div>
        </div>

        <div className="col-span-2 flex flex-wrap items-center justify-between gap-x-[var(--gg-space-3)] gap-y-[var(--gg-space-2)] sm:col-span-1 sm:col-start-2">
          <QuantityStepper
            value={line.quantity}
            min={1}
            onChange={onQuantityChange}
            disabled={busy}
            aria-label={`${line.name} 數量`}
          />
          <div className="ml-auto flex min-w-0 items-baseline gap-[var(--gg-space-2)]">
            <span className="shrink-0 text-[length:var(--gg-text-xs)] text-fg-muted">小計</span>
            <PriceDisplay amount={line.lineTotal} size="md" />
          </div>
        </div>
      </div>

      {line.availabilityWarning && (
        <p className="flex items-start gap-[var(--gg-space-2)] break-words text-[length:var(--gg-text-sm)] text-danger">
          <IconAlertTriangle aria-hidden className="shrink-0" />
          <span className="min-w-0">{line.availabilityWarning}</span>
        </p>
      )}
    </div>
  );
}
