'use client';

import { useEffect, useState } from 'react';
import { cn } from './internal/cn';

export interface CountdownProps {
  /** 後端給的截團時間（ISO 字串）。 */
  closesAt: string;
  /**
   * 能不能下單看這個，**不是看倒數有沒有歸零**——
   * 截團是後端 Saga Timer 說了算，客戶端時鐘不可信。
   */
  isAcceptingOrders: boolean;
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

/** 倒數純粹是畫面回饋；下單能不能按永遠看 `isAcceptingOrders`，不是這個元件的輸出。 */
export function Countdown({ closesAt, isAcceptingOrders, className }: CountdownProps) {
  const target = new Date(closesAt).getTime();
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const interval = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(interval);
  }, []);

  if (!isAcceptingOrders) {
    return (
      <span
        className={cn(
          'font-display text-[length:var(--gg-text-sm)] font-bold text-fg-muted',
          className,
        )}
      >
        已截團
      </span>
    );
  }

  return (
    <span
      role="timer"
      className={cn(
        'inline-flex items-center gap-[var(--gg-space-1)] font-display text-[length:var(--gg-text-sm)] font-bold text-danger tabular-nums',
        className,
      )}
    >
      {formatRemaining(target - now)} 後截團
    </span>
  );
}
