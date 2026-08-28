import type { ReactNode } from 'react';
import { InboxIcon } from './icons';

export interface EmptyStateProps {
  readonly title: string;
  readonly description?: string | undefined;
  readonly action?: ReactNode;
  readonly icon?: ReactNode;
  readonly className?: string;
}

export function EmptyState({ title, description, action, icon, className }: EmptyStateProps) {
  return (
    <div
      className={`flex flex-col items-center justify-center gap-2 px-6 py-12 text-center ${className ?? ''}`.trim()}
    >
      <div className="text-fg-subtle">{icon ?? <InboxIcon className="h-10 w-10" />}</div>
      <p className="text-sm font-medium text-fg">{title}</p>
      {description ? <p className="max-w-sm text-sm text-fg-muted">{description}</p> : null}
      {action ? <div className="mt-2">{action}</div> : null}
    </div>
  );
}
