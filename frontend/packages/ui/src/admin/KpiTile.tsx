import type { ReactNode } from 'react';
import type { StatusTone } from './StatusPill';

export interface KpiTileProps {
  readonly label: string;
  /** 已經格式化好的字串（`formatMoney` 或純數字），這裡只負責排版。 */
  readonly value: string;
  readonly hint?: string;
  readonly tone?: StatusTone;
  readonly icon?: ReactNode;
  readonly className?: string;
}

const TONE_VALUE_CLASS: Record<StatusTone, string> = {
  neutral: 'text-fg',
  info: 'text-info',
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
};

export function KpiTile({ label, value, hint, tone = 'neutral', icon, className }: KpiTileProps) {
  return (
    <div
      className={`rounded-card border border-border-soft bg-surface p-4 shadow-card ${className ?? ''}`.trim()}
    >
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium text-fg-muted">{label}</p>
        {icon ? <div className="text-fg-subtle">{icon}</div> : null}
      </div>
      <p className={`mt-2 font-mono text-2xl font-semibold tabular-nums ${TONE_VALUE_CLASS[tone]}`}>
        {value}
      </p>
      {hint ? <p className="mt-1 text-xs text-fg-muted">{hint}</p> : null}
    </div>
  );
}
