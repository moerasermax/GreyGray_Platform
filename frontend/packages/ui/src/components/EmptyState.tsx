import { cn } from './internal/cn';
import { IconInbox } from './icons';

export interface EmptyStateProps {
  icon?: React.ReactNode;
  title: string;
  description?: string | undefined;
  action?: React.ReactNode;
  className?: string;
}

/** 圖示 ＋ 一句說明 ＋ 一個行動。給 `DataTable` 0 列、列表沒有結果等情境共用。 */
export function EmptyState({ icon, title, description, action, className }: EmptyStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center gap-[var(--gg-space-3)] p-[var(--gg-space-7)] text-center',
        className,
      )}
    >
      <span className="flex items-center justify-center rounded-pill bg-surface-sunken p-[var(--gg-space-4)] text-[length:var(--gg-text-2xl)] text-fg-muted">
        {icon ?? <IconInbox />}
      </span>
      <p className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{title}</p>
      {description && (
        <p className="max-w-sm text-[length:var(--gg-text-sm)] text-fg-muted">{description}</p>
      )}
      {action && <div className="mt-[var(--gg-space-2)]">{action}</div>}
    </div>
  );
}
