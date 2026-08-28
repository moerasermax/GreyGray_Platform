'use client';

import { Badge, IconAlertTriangle, IconButton, IconX, PriceDisplay, QuantityStepper } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface CartLineRowProps {
  line: S['CartLine'];
  onQuantityChange: (quantity: number) => void;
  onRemove: () => void;
  busy?: boolean;
}

/**
 * `ProductCard` 已知偏離用原生 `<img>`（見交付回報），這裡的縮圖跟著同一個做法，
 * 不在這個路徑自己重新發明一套圖片載入方式。
 */
export function CartLineRow({ line, onQuantityChange, onRemove, busy = false }: CartLineRowProps) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-3)] border-b border-border-soft py-[var(--gg-space-4)] last:border-b-0">
      <div className="flex gap-[var(--gg-space-3)]">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={line.imageUrl ?? undefined}
          alt=""
          className="h-16 w-16 shrink-0 rounded-[var(--gg-radius-sm)] bg-surface-sunken object-cover"
        />

        <div className="flex flex-1 flex-col gap-[var(--gg-space-1)]">
          <div className="flex items-start justify-between gap-[var(--gg-space-2)]">
            <div>
              <p className="font-display text-[length:var(--gg-text-base)] font-bold text-fg">{line.name}</p>
              {line.variantName && <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{line.variantName}</p>}
            </div>
            <IconButton icon={<IconX />} aria-label="移除這個品項" size="sm" onClick={onRemove} disabled={busy} />
          </div>

          <div className="flex items-center gap-[var(--gg-space-2)]">
            <Badge variant={line.mode} label={line.mode === 'Stock' ? '現貨' : '預購'} />
            <PriceDisplay amount={line.unitPrice} size="sm" />
          </div>

          <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
            <QuantityStepper
              value={line.quantity}
              min={1}
              onChange={onQuantityChange}
              disabled={busy}
              aria-label={`${line.name} 數量`}
            />
            <PriceDisplay amount={line.lineTotal} size="md" />
          </div>
        </div>
      </div>

      {line.availabilityWarning && (
        <p className="flex items-center gap-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] text-danger">
          <IconAlertTriangle aria-hidden />
          {line.availabilityWarning}
        </p>
      )}
    </div>
  );
}
