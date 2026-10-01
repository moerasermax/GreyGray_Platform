'use client';

import { useEffect, useRef, useState } from 'react';
import type { components } from '@greygray/api-client/storefront';
import { Card } from '@greygray/ui';
import {
  COPY_ORDER_NUMBER_IDLE,
  COPY_ORDER_NUMBER_SUCCEEDED,
  copyText,
} from '../../../(checkout)/_lib/paymentResultSummary';
import { paymentInstructionsDisplay } from '../../../(checkout)/_lib/paymentInstructions';

type S = components['schemas'];

export interface PaymentInstructionsCardProps {
  order: Pick<S['Order'], 'paymentInstructions' | 'grandTotal'>;
}

export function PaymentInstructionsCard({ order }: PaymentInstructionsCardProps) {
  const display = paymentInstructionsDisplay(order);
  const [copyFeedback, setCopyFeedback] = useState('');
  const copyResetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(
    () => () => {
      if (copyResetTimerRef.current !== null) clearTimeout(copyResetTimerRef.current);
    },
    [],
  );

  if (display == null) return null;

  async function handleCopy(value: string) {
    const writeText = globalThis.navigator?.clipboard?.writeText.bind(globalThis.navigator.clipboard);
    const result = await copyText(value, writeText);
    setCopyFeedback(result);

    if (copyResetTimerRef.current !== null) clearTimeout(copyResetTimerRef.current);
    if (result === COPY_ORDER_NUMBER_SUCCEEDED) {
      copyResetTimerRef.current = setTimeout(() => setCopyFeedback(''), 2_000);
    }
  }

  return (
    <Card className="flex flex-col gap-[var(--gg-space-4)]">
      <div className="flex flex-col gap-[var(--gg-space-2)]">
        <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{display.title}</h2>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          這筆訂單已產生繳費資訊，請不要重新付款。
        </p>
      </div>

      <dl className="flex flex-col gap-[var(--gg-space-3)]">
        {display.rows.map((row) => (
          <div key={row.key} className="flex flex-col gap-[var(--gg-space-1)]">
            <dt className="text-[length:var(--gg-text-xs)] font-bold text-fg-muted">{row.label}</dt>
            <dd className="flex flex-wrap items-center gap-[var(--gg-space-2)]">
              <span className="select-text break-all font-bold text-fg">{row.value}</span>
              {row.copyable && (
                <button
                  type="button"
                  className="min-h-[var(--gg-touch-min)] px-[var(--gg-space-3)] font-bold text-primary-text underline underline-offset-2"
                  aria-label={`複製${row.label}`}
                  onClick={() => void handleCopy(row.value)}
                >
                  {COPY_ORDER_NUMBER_IDLE}
                </button>
              )}
            </dd>
          </div>
        ))}
      </dl>

      <div className="flex flex-col gap-[var(--gg-space-1)] text-[length:var(--gg-text-sm)] text-fg-muted">
        <p>繳費完成後，系統收到通知會自動更新訂單，可能需要一些時間。</p>
        <p>逾期未繳，訂單會自動取消。</p>
      </div>
      <p aria-live="polite" className="text-[length:var(--gg-text-sm)] text-fg-muted">
        {copyFeedback}
      </p>
    </Card>
  );
}
