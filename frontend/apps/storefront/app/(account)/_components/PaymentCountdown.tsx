'use client';

import { useEffect, useRef, useState } from 'react';
import { cn } from './cn';

export interface PaymentCountdownProps {
  /** 後端算好的付款期限（ISO 字串）。逾期未付會被 Saga Timer 自動取消。 */
  paymentDueAt: string;
  /**
   * 倒數歸零時呼叫。**這裡只負責通知，不負責改狀態**——
   * 呼叫端要重新 `GET /v1/orders/{id}` 拿後端當下真正的狀態，
   * 客戶端時鐘不可信，狀態永遠是後端說了算。
   */
  onExpire: () => void;
  className?: string;
}

function formatRemaining(ms: number): string {
  if (ms <= 0) return '00:00:00';
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return [hours, minutes, seconds].map((n) => String(n).padStart(2, '0')).join(':');
}

export function PaymentCountdown({ paymentDueAt, onExpire, className }: PaymentCountdownProps) {
  const target = new Date(paymentDueAt).getTime();
  const [now, setNow] = useState(() => Date.now());
  const firedRef = useRef(false);

  useEffect(() => {
    firedRef.current = false;
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, [paymentDueAt]);

  useEffect(() => {
    if (target - now <= 0 && !firedRef.current) {
      firedRef.current = true;
      onExpire();
    }
  }, [now, target, onExpire]);

  const remaining = target - now;
  const isExpiring = remaining > 0 && remaining <= 5 * 60_000;

  return (
    <span
      role="timer"
      className={cn(
        'inline-flex items-center gap-[var(--gg-space-1)] font-display text-[length:var(--gg-text-sm)] font-bold tabular-nums',
        isExpiring || remaining <= 0 ? 'text-danger' : 'text-fg',
        className,
      )}
    >
      剩 {formatRemaining(remaining)} 內付款，逾期將自動取消
    </span>
  );
}
