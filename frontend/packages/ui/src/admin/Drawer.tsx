'use client';

import type { ReactNode } from 'react';
import { CloseIcon } from './icons';
import { useFocusTrap } from './useFocusTrap';

export interface DrawerProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly title: string;
  readonly children?: ReactNode;
  readonly footer?: ReactNode;
}

/** 從右側滑入的面板，用來看一筆資料的明細。focus trap、Esc 關閉。 */
export function Drawer({ open, onClose, title, children, footer }: DrawerProps) {
  const containerRef = useFocusTrap(open, onClose);

  if (!open) return null;

  return (
    <div className="fixed inset-0" style={{ zIndex: 'var(--ga-z-modal)' }}>
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden="true" />
      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="gg-drawer-title"
        tabIndex={-1}
        className="absolute inset-y-0 right-0 flex w-full max-w-md flex-col border-l border-border-soft bg-surface shadow-popover outline-none"
      >
        <div className="flex items-center justify-between border-b border-border-soft px-5 py-4">
          <h2 id="gg-drawer-title" className="text-lg font-semibold text-fg">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="關閉"
            className="rounded-sm p-1 text-fg-muted hover:bg-surface-sunken hover:text-fg"
          >
            <CloseIcon />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer ? (
          <div className="flex justify-end gap-2 border-t border-border-soft px-5 py-4">{footer}</div>
        ) : null}
      </div>
    </div>
  );
}
