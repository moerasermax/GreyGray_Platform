import type { ReactNode } from 'react';

export interface FilterBarProps {
  readonly children: ReactNode;
  /** 靠右的動作區，例如「匯出」「新增」按鈕。 */
  readonly actions?: ReactNode;
  readonly className?: string;
}

export function FilterBar({ children, actions, className }: FilterBarProps) {
  return (
    <div
      className={`flex flex-wrap items-end justify-between gap-3 rounded-card border border-border-soft bg-surface p-3 shadow-card ${className ?? ''}`.trim()}
    >
      <div className="flex flex-wrap items-end gap-3">{children}</div>
      {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
    </div>
  );
}
