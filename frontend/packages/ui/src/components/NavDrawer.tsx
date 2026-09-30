'use client';

import { useRef } from 'react';
import { createPortal } from 'react-dom';
import { IconButton } from './IconButton';
import { IconX } from './icons';
import { useFocusTrap } from './internal/useFocusTrap';
import { usePortalTarget } from './internal/usePortalTarget';
import { useScrollLock } from './internal/useScrollLock';

export {
  navDrawerLockScroll,
  navDrawerRestoreScroll,
} from './internal/useScrollLock';

export interface NavDrawerProps {
  open: boolean;
  onClose: () => void;
  id: string;
  title: string;
  closeLabel: string;
  children: React.ReactNode;
}

/** 手機導覽抽屜：portal、焦點圈限、Esc 關閉，並從左側滑入。 */
export function NavDrawer({
  open,
  onClose,
  id,
  title,
  closeLabel,
  children,
}: NavDrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, open, onClose);
  useScrollLock(open);
  const portalTarget = usePortalTarget();
  if (!portalTarget) return null;

  return createPortal(
    <div
      inert={!open}
      className={
        'fixed inset-0 z-[var(--gg-z-modal)] transition-opacity ' +
        'duration-[var(--gg-duration-base)] ease-out-soft ' +
        (open ? 'pointer-events-auto opacity-100' : 'pointer-events-none opacity-0')
      }
      aria-hidden={!open}
    >
      <div className="absolute inset-0 bg-fg/40" onClick={onClose} />
      <div
        ref={panelRef}
        id={id}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className={
          'absolute inset-y-0 left-0 flex w-[var(--gg-drawer-width)] flex-col bg-surface shadow-raised ' +
          'transition-transform duration-[var(--gg-duration-base)] ease-out-soft ' +
          (open ? 'translate-x-0' : '-translate-x-full')
        }
      >
        <div
          className="flex items-center justify-between gap-[var(--gg-space-3)] border-b border-border p-[var(--gg-space-4)]"
          style={{ paddingTop: 'calc(var(--gg-space-4) + env(safe-area-inset-top, 0px))' }}
        >
          <h2 className="font-display text-[length:var(--gg-text-xl)] font-bold text-fg">
            {title}
          </h2>
          <IconButton icon={<IconX />} aria-label={closeLabel} size="sm" onClick={onClose} />
        </div>
        <div className="min-h-0 flex-1 overscroll-contain overflow-y-auto p-[var(--gg-space-4)]">
          {children}
        </div>
      </div>
    </div>,
    portalTarget,
  );
}
