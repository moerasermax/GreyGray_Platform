'use client';

import { useEffect } from 'react';
import { cn } from './internal/cn';
import { IconButton } from './IconButton';
import { IconAlertCircle, IconCheckCircle, IconX } from './icons';

export type ToastVariant = 'success' | 'error';

export interface ToastAction {
  /** 按鈕上的字。要講得出去哪裡，「查看購物車」而不是「確定」。 */
  label: string;
  /** 去哪裡。真的 `<a href>`，右鍵可以在新分頁開啟、鍵盤到得了。 */
  href: string;
}

export interface ToastProps {
  open: boolean;
  variant: ToastVariant;
  message: string;
  onClose?: (() => void) | undefined;
  /** 自動消失的毫秒數。不給就不自動消失，只能靠 onClose。 */
  duration?: number | undefined;
  /**
   * **選填**的動作連結（FE-24 為 #32 加的，後台既有的用法完全不受影響）。
   *
   * ⚠ 給了 `action` 就要把 `duration` 拉長：**按不到的按鈕比沒有按鈕更糟**。
   * 提示預設 2.5 秒消失，那是「看一眼就好」的長度，不是「移動手指過去按」的長度。
   * 這裡不自作主張改寫 `duration`（呼叫端才知道自己的情境），改成用測試擋：
   * 前台的 `app/_lib/__tests__/toastActionDuration.test.ts` 去原始碼掃每一個
   * `<Toast>`，有 `action` 的呼叫點 `duration` 一律要 ≥ 5000。
   */
  action?: ToastAction | undefined;
  className?: string;
}

/** `role="status"`，成功／失敗兩種。呼叫端決定 open，這裡只負責畫面與自動消失計時。 */
export function Toast({
  open,
  variant,
  message,
  onClose,
  duration,
  action,
  className,
}: ToastProps) {
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
      {action && (
        /*
         * 純 `<a>`，不用 `next/link`：`@greygray/ui` 兩個 app 共用，
         * 不可以依賴 Next 的 router。站內導覽會多一次整頁載入，
         * 但這是提示裡的一顆按鈕，換到的是「這個元件在哪都能用」。
         */
        <a
          href={action.href}
          className={cn(
            'shrink-0 whitespace-nowrap rounded-pill px-[var(--gg-space-3)] py-[var(--gg-space-1)]',
            'text-[length:var(--gg-text-sm)] font-bold text-primary-text underline',
            'transition-colors duration-[var(--gg-duration-base)] ease-out-soft hover:bg-surface-sunken',
          )}
        >
          {action.label}
        </a>
      )}
      {onClose && (
        <IconButton icon={<IconX />} aria-label="關閉通知" size="sm" onClick={onClose} />
      )}
    </div>
  );
}
