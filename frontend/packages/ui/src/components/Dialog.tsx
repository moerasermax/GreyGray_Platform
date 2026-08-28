'use client';

import { useRef } from 'react';
import { cn } from './internal/cn';
import { useFocusTrap } from './internal/useFocusTrap';
import { IconButton } from './IconButton';
import { IconX } from './icons';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title?: string | undefined;
  children: React.ReactNode;
  footer?: React.ReactNode;
  className?: string;
}

/**
 * 置中對話框。focus trap、Esc 關、關閉後焦點回到觸發元素。
 *
 * 不用 `createPortal`——這個套件目前無法宣告 `react-dom` 依賴（沒有
 * `@types/react-dom` 可解析，見 FE-2 交付說明），改用 `position: fixed`
 * 直接疊在原本的 DOM 位置。副作用：父層若有 `overflow: hidden` 或
 * CSS transform 會裁切它，一般頁面不會遇到。
 */
export function Dialog({ open, onClose, title, children, footer, className }: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open, onClose);

  return (
    <div
      inert={!open}
      className={cn(
        'fixed inset-0 z-[var(--gg-z-modal)] flex items-center justify-center',
        'p-[var(--gg-space-4)] transition-opacity duration-[var(--gg-duration-base)] ease-out-soft',
        open ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0',
      )}
      aria-hidden={!open}
    >
      <div
        className="absolute inset-0 bg-fg/40"
        onClick={onClose}
      />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className={cn(
          'relative w-full max-w-md rounded-[var(--gg-radius-lg)] bg-surface p-[var(--gg-space-5)] shadow-raised',
          'transition-transform duration-[var(--gg-duration-base)] ease-out-soft',
          open ? 'scale-100' : 'scale-95',
          className,
        )}
      >
        <div className="flex items-start justify-between gap-[var(--gg-space-3)]">
          {title && (
            <h2 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">
              {title}
            </h2>
          )}
          <IconButton
            icon={<IconX />}
            aria-label="關閉"
            size="sm"
            className="ml-auto"
            onClick={onClose}
          />
        </div>

        <div className="mt-[var(--gg-space-4)]">{children}</div>

        {footer && (
          <div className="mt-[var(--gg-space-5)] flex justify-end gap-[var(--gg-space-2)]">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
