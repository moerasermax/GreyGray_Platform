import { cn } from './internal/cn';
import { IconAlertTriangle } from './icons';
import { Button } from './Button';

export interface ErrorStateProps {
  /** 直接傳 `ApiError.problem.title`——這個套件不依賴 `@greygray/api-client`（見 FE-2 交付說明）。 */
  title: string;
  /** 完整 traceId；這裡只顯示後 8 碼給客人報給客服，不用先自己截。 */
  traceId?: string | null | undefined;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({ title, traceId, onRetry, className }: ErrorStateProps) {
  const shortTraceId = traceId ? traceId.slice(-8) : null;

  return (
    <div
      className={cn(
        'flex flex-col items-center gap-[var(--gg-space-3)] p-[var(--gg-space-7)] text-center',
        className,
      )}
    >
      <span className="flex items-center justify-center rounded-pill bg-danger/10 p-[var(--gg-space-4)] text-[length:var(--gg-text-2xl)] text-danger">
        <IconAlertTriangle />
      </span>
      <p className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{title}</p>
      {shortTraceId && (
        <p className="text-[length:var(--gg-text-xs)] text-fg-muted">
          參考代碼：{shortTraceId}（可提供給客服查詢）
        </p>
      )}
      {onRetry && (
        <Button variant="secondary" onClick={onRetry} className="mt-[var(--gg-space-2)]">
          重試
        </Button>
      )}
    </div>
  );
}
