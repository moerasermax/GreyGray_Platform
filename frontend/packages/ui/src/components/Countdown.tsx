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

/**
 * 還不知道現在幾點時的佔位。
 *
 * 用 `--:--:--` 而不是 `00:00:00`：後者會被讀成「已經歸零」，
 * 那是這個專案的紅線（畫面不可以宣稱不成立的事）。
 * 寬度靠 `tabular-nums` ＋ 同樣八個字元維持穩定，掛載後換成真數字不會跳版。
 */
const UNKNOWN_REMAINING = '--:--:--';

/** 倒數純粹是畫面回饋；下單能不能按永遠看 `isAcceptingOrders`，不是這個元件的輸出。 */
export function Countdown({ closesAt, isAcceptingOrders, className }: CountdownProps) {
  const target = new Date(closesAt).getTime();
  /*
   * ── #31：`now` 的初始值一定要是 `null`，不可以是 `Date.now()` ──
   * 伺服器算一次、瀏覽器 hydration 時再算一次，兩邊的秒數必然不同
   * （2026-09-02 console 逐字證據：`+137:02:24` / `-137:02:25`），
   * React 判定文字不一致就把**整棵樹丟掉重建**——功能沒壞，但每次載入
   * 多一次整頁 client 重建，而且真正的 hydration 錯誤會被這一條淹掉。
   *
   * 所以伺服器與瀏覽器的第一次渲染都畫佔位（兩邊逐字相同 → 不會不一致），
   * `useEffect` 只在瀏覽器跑，掛載後才開始算。
   */
  const [now, setNow] = useState<number | null>(null);

  useEffect(() => {
    setNow(Date.now());
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
      {now === null ? UNKNOWN_REMAINING : formatRemaining(target - now)} 後截團
    </span>
  );
}
