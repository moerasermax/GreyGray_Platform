/**
 * 訂單／開團／付款狀態共用的狀態標籤。
 *
 * 呼叫端傳 `tone` 決定顏色（呼叫端最清楚自己那個 enum 的語意），
 * 這裡不內建 enum 對照表——不同模組的狀態字串會撞名（例如都可能有 `Cancelled`），
 * 硬綁在元件裡反而讓別人沒辦法覆寫語意。**未知值一律退回顯示原始字串**，tone 預設 `neutral`。
 */
export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export interface StatusPillProps {
  readonly label: string;
  readonly tone?: StatusTone;
  readonly className?: string;
}

const TONE_CLASS: Record<StatusTone, string> = {
  neutral: 'bg-surface-sunken text-fg-on-tint border border-border-soft',
  info: 'bg-info-subtle text-info',
  success: 'bg-success-subtle text-success',
  warning: 'bg-warning-subtle text-warning',
  danger: 'bg-danger-subtle text-danger',
};

export function StatusPill({ label, tone = 'neutral', className }: StatusPillProps) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ${TONE_CLASS[tone]} ${className ?? ''}`.trim()}
    >
      {label}
    </span>
  );
}
