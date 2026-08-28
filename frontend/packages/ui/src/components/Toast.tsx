'use client';

import { useEffect } from 'react';
import { cn } from './internal/cn';
import { IconButton } from './IconButton';
import { IconAlertCircle, IconCheckCircle, IconX } from './icons';

export type ToastVariant = 'success' | 'error';

export interface ToastProps {
  open: boolean;
  variant: ToastVariant;
  message: string;
  onClose?: (() => void) | undefined;
  /** 自動消失的毫秒數。不給就不自動消失，只能靠 onClose。 */
  duration?: number | undefined;
  className?: string;
}

/** `role="status"`，成功／失敗兩種。呼叫端決定 open，這裡只負責畫面與自動消失計時。 */
export function Toast({ open, variant, message, onClose, duration, className }: ToastProps) {
  useEffect(() => {
    if (!open || !duration || !onClose) return;
    const timer = setTimeout(onClose, duration);
    return () => clearTimeout(timer);
  }, [open, duration, onClose]);

  if (!open) return null;

  const isSuccess = variant === 'success';

  return (
    <div
      role="status"
      aria-live="polite"
      className={cn(
        'flex items-center gap-[var(--gg-space-3)] rounded-card bg-surface p-[var(--gg-space-4)] shadow-raised',
        'border',
        isSuccess ? 'border-success/30' : 'border-danger/30',
        className,
      )}
    >
      {isSuccess ? (
        <IconCheckCircle className="shrink-0 text-[length:var(--gg-text-xl)] text-success" />
      ) : (
        <IconAlertCircle className="shrink-0 text-[length:var(--gg-text-xl)] text-danger" />
      )}
      <p className="flex-1 text-[length:var(--gg-text-sm)] text-fg">{message}</p>
      {onClose && (
        <IconButton icon={<IconX />} aria-label="關閉通知" size="sm" onClick={onClose} />
      )}
    </div>
  );
}
