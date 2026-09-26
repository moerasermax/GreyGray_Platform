'use client';

import type { components } from '@greygray/api-client/storefront';
import { SHIPPING_POLICY_HINT, SHIPPING_POLICY_LABEL } from '../_lib/labels';
import { cn } from '../_lib/cn';

type S = components['schemas'];

const POLICIES: readonly S['ShippingPolicy'][] = ['ShipSeparately', 'HoldUntilComplete'];

export interface ShippingPolicyPickerProps {
  value: S['ShippingPolicy'] | null;
  onChange: (policy: S['ShippingPolicy']) => void;
}

/**
 * 混合訂單（`hasMixedModes = true`）專用。**下單時就要問，不是出貨時才問**——
 * 兩個選項的差別要說清楚：現貨先出付兩次運費／等回國一起出省一次（docs/06 FE-4）。
 */
export function ShippingPolicyPicker({ value, onChange }: ShippingPolicyPickerProps) {
  return (
    <div role="radiogroup" aria-label="出貨方式" className="flex flex-col gap-[var(--gg-space-3)]">
      <p className="text-[length:var(--gg-text-sm)] font-bold text-fg">
        這筆訂單同時有現貨與預購商品，出貨方式要現在選：
      </p>
      {POLICIES.map((policy) => {
        const selected = value === policy;
        return (
          <button
            key={policy}
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={() => onChange(policy)}
            className={cn(
              'flex flex-col gap-[var(--gg-space-1)] rounded-card border p-[var(--gg-space-4)] text-left',
              'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft',
              selected ? 'border-primary-strong bg-primary-subtle' : 'border-border-soft bg-surface hover:bg-surface-sunken',
            )}
          >
            <span className="font-display text-[length:var(--gg-text-base)] font-bold text-fg">
              {SHIPPING_POLICY_LABEL[policy]}
            </span>
            <span className="text-[length:var(--gg-text-sm)] text-fg-muted">
              {SHIPPING_POLICY_HINT[policy]}
            </span>
          </button>
        );
      })}
    </div>
  );
}
