'use client';

import { useRef } from 'react';
import { createPortal } from 'react-dom';
import { cn } from './internal/cn';
import { useFocusTrap } from './internal/useFocusTrap';
import { usePortalTarget } from './internal/usePortalTarget';
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
 * 用 `createPortal` 掛到 `document.body`。只靠 `position: fixed` 疊在原地是不夠的——
 * 父層只要有 `overflow: hidden` 或任何 CSS transform 就會建立新的包含區塊，
 * 對話框會被裁掉。那種父層在卡片牆與橫捲容器裡到處都是。
 */
export function Dialog({ open, onClose, title, children, footer, className }: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open, onClose);
  const portalTarget = usePortalTarget();
  if (!portalTarget) return null;

  return createPortal(
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
    </div>,
    portalTarget,
  );
}
