import { Badge } from '@greygray/ui';
import { buildOrderTimeline, orderStatusLabel } from '../_lib/orderStatus';
import { cn } from './cn';

export interface OrderTimelineProps {
  status: string;
  className?: string;
}

/**
 * 訂單狀態時間軸。九個狀態共用一條主線，`Cancelled` 不畫在主線上——
 * 改成一則獨立的取消提示，避免看起來像「走到了最後一步」。
 */
export function OrderTimeline({ status, className }: OrderTimelineProps) {
  const timeline = buildOrderTimeline(status);

  if (timeline.isCancelled) {
    return (
      <div className={cn('flex items-center gap-[var(--gg-space-3)]', className)}>
        <Badge variant="Cancelled" label="已取消" />
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">這張訂單已經取消，不會再往下進行。</p>
      </div>
    );
  }

  return (
    <ol className={cn('flex flex-col gap-0', className)}>
      {timeline.steps.map((step, index) => {
        const isLast = index === timeline.steps.length - 1;
        return (
          <li key={step.status} className="flex gap-[var(--gg-space-3)]">
            <div className="flex flex-col items-center">
              <span
                aria-hidden
                className={cn(
                  'h-[10px] w-[10px] shrink-0 rounded-pill',
                  step.state === 'upcoming' ? 'bg-surface-sunken border border-border-soft' : 'bg-primary',
                )}
              />
              {!isLast && (
                <span
                  aria-hidden
                  className={cn('w-px flex-1', step.state === 'done' ? 'bg-primary' : 'bg-border-soft')}
                  style={{ minHeight: 'var(--gg-space-5)' }}
                />
              )}
            </div>
            <div className={cn('pb-[var(--gg-space-4)]', step.state === 'upcoming' && 'opacity-60')}>
              <p
                className={cn(
                  'text-[length:var(--gg-text-sm)]',
                  step.state === 'current' ? 'font-bold text-primary-text' : 'text-fg',
                )}
              >
                {step.label}
              </p>
              {step.state === 'current' && (
                <p className="text-[length:var(--gg-text-xs)] text-fg-muted">目前狀態</p>
              )}
            </div>
          </li>
        );
      })}
      {/* 未知狀態（後端新增了主線之外的值）：不要憑空塞進主線，另外標示原始字串。 */}
      {!timeline.steps.some((s) => s.status === timeline.rawStatus) && timeline.rawStatus !== 'Cancelled' && (
        <li className="mt-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] text-fg-muted">
          目前狀態代碼：{orderStatusLabel(timeline.rawStatus)}
        </li>
      )}
    </ol>
  );
}
