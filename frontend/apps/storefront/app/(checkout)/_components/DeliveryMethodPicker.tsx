'use client';

import { BottomSheet, Button, Spinner } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { DELIVERY_METHOD_HINT, DELIVERY_METHOD_LABEL } from '../_lib/labels';
import { cn } from '../_lib/cn';

type S = components['schemas'];

const METHODS: readonly S['DeliveryMethod'][] = ['ConvenienceStore', 'HomeDelivery', 'SelfPickup'];

export interface DeliveryMethodPickerProps {
  open: boolean;
  onClose: () => void;
  value: S['DeliveryMethod'] | null;
  onSelect: (method: S['DeliveryMethod']) => void;
  /** 正在等 `/v1/cart/quote` 回來的那個 method；讓使用者知道點了但還在算。 */
  quoting: S['DeliveryMethod'] | null;
}

/** 配送方式選單。M1a 只有三種一口價，選了就重新詢價（docs/06 FE-4：純函式端點，放心多打）。 */
export function DeliveryMethodPicker({ open, onClose, value, onSelect, quoting }: DeliveryMethodPickerProps) {
  return (
    <BottomSheet open={open} onClose={onClose} title="選擇配送方式">
      <div className="flex flex-col gap-[var(--gg-space-3)]">
        {METHODS.map((method) => {
          const selected = value === method;
          const isQuoting = quoting === method;
          return (
            <button
              key={method}
              type="button"
              disabled={isQuoting}
              onClick={() => onSelect(method)}
              aria-pressed={selected}
              className={cn(
                'flex items-center justify-between gap-[var(--gg-space-3)] rounded-card border p-[var(--gg-space-4)] text-left',
                'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft disabled:opacity-60',
                selected ? 'border-primary-strong bg-primary-subtle' : 'border-border-soft bg-surface hover:bg-surface-sunken',
              )}
            >
              <span className="flex flex-col gap-[var(--gg-space-1)]">
                <span className="font-display text-[length:var(--gg-text-base)] font-bold text-fg">
                  {DELIVERY_METHOD_LABEL[method]}
                </span>
                <span className="text-[length:var(--gg-text-sm)] text-fg-muted">
                  {DELIVERY_METHOD_HINT[method]}
                </span>
              </span>
              {isQuoting && <Spinner label="計算運費中" />}
            </button>
          );
        })}
      </div>
      <Button variant="ghost" fullWidth onClick={onClose} className="mt-[var(--gg-space-4)]">
        關閉
      </Button>
    </BottomSheet>
  );
}
