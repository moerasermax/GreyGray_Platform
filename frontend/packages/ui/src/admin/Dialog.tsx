'use client';

import type { ReactNode } from 'react';
import { CloseIcon } from './icons';
import { useFocusTrap } from './useFocusTrap';

export interface DialogProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly title: string;
  readonly description?: string;
  readonly children?: ReactNode;
  readonly footer?: ReactNode;
}

/** 置中彈窗。focus trap、Esc 關閉、關閉後焦點還給觸發元素。 */
export function Dialog({ open, onClose, title, description, children, footer }: DialogProps) {
  const containerRef = useFocusTrap(open, onClose);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 flex items-center justify-center p-4"
      style={{ zIndex: 'var(--ga-z-modal)' }}
    >
      <div className="absolute inset-0 bg-black/40" onClick={onClose} aria-hidden="true" />
      <div
        ref={containerRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="gg-dialog-title"
        tabIndex={-1}
        className="relative z-10 w-full max-w-md rounded-lg border border-border-soft bg-surface p-5 shadow-popover outline-none"
      >
        <div className="flex items-start justify-between gap-4">
          <h2 id="gg-dialog-title" className="text-lg font-semibold text-fg">
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
        {description ? <p className="mt-2 text-sm text-fg-muted">{description}</p> : null}
        {children ? <div className="mt-4">{children}</div> : null}
        {footer ? <div className="mt-5 flex justify-end gap-2">{footer}</div> : null}
      </div>
    </div>
  );
}
