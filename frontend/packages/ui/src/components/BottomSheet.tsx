'use client';

import { useRef } from 'react';
import { cn } from './internal/cn';
import { useFocusTrap } from './internal/useFocusTrap';
import { IconButton } from './IconButton';
import { IconX } from './icons';

export interface BottomSheetProps {
  open: boolean;
  onClose: () => void;
  title?: string | undefined;
  children: React.ReactNode;
  className?: string;
}

/**
 * 手機上的選項面板。Esc 關、focus trap，從底部滑入。
 *
 * 不用 `createPortal`，理由同 `Dialog.tsx`——這個套件目前無法宣告
 * `react-dom` 依賴，改用 `position: fixed`。
 */
export function BottomSheet({ open, onClose, title, children, className }: BottomSheetProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open, onClose);

  return (
    <div
      inert={!open}
      className={cn(
        'fixed inset-0 z-[var(--gg-z-modal)] transition-opacity duration-[var(--gg-duration-base)] ease-out-soft',
        open ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0',
      )}
      aria-hidden={!open}
    >
      <div className="absolute inset-0 bg-fg/40" onClick={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className={cn(
          'absolute inset-x-0 bottom-0 max-h-[var(--gg-sheet-max-h)] overflow-y-auto rounded-t-[var(--gg-radius-xl)]',
          'bg-surface p-[var(--gg-space-5)] shadow-raised',
          'transition-transform duration-[var(--gg-duration-base)] ease-out-soft',
          open ? 'translate-y-0' : 'translate-y-full',
          className,
        )}
        style={{ paddingBottom: 'calc(var(--gg-space-5) + env(safe-area-inset-bottom, 0px))' }}
      >
        <div className="mx-auto mb-[var(--gg-space-3)] h-[var(--gg-space-1)] w-[var(--gg-space-7)] rounded-pill bg-border-strong" />

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
      </div>
    </div>
  );
}
