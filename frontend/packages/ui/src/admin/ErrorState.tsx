import { ErrorIcon } from './icons';

/**
 * 呼叫端傳 `error.problem.title` 與 `error.shortTraceId`（`ApiError` 的既有 getter），
 * 這裡不依賴 `@greygray/api-client`——`packages/ui` 保持跟傳輸層無關。
 */
export interface ErrorStateProps {
  readonly title: string;
  /** `ApiError.shortTraceId`：traceId 後 8 碼，讓使用者可以報給工程。 */
  readonly traceId?: string | null;
  readonly onRetry?: () => void;
  readonly className?: string;
}

export function ErrorState({ title, traceId, onRetry, className }: ErrorStateProps) {
  return (
    <div
      role="alert"
      className={`flex flex-col items-center justify-center gap-2 px-6 py-12 text-center ${className ?? ''}`.trim()}
    >
      <ErrorIcon className="h-10 w-10 text-danger" />
      <p className="text-sm font-medium text-fg">{title}</p>
      {traceId ? (
        <p className="gg-numeric text-xs text-fg-subtle">追蹤碼 {traceId}</p>
      ) : null}
      {onRetry ? (
        <button
          type="button"
          onClick={onRetry}
          className="mt-2 rounded-sm border border-border-strong px-3 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken"
        >
          重試
        </button>
      ) : null}
    </div>
  );
}
