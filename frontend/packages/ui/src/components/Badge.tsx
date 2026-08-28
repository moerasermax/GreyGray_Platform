import { cn } from './internal/cn';

/** 已知值給明確樣式；`(string & {})` 讓 TS 還是能自動完成，但不排斥後端加新值。 */
export type BadgeVariant = 'New' | 'Popular' | 'LastCall' | (string & {});

export interface BadgeProps extends Omit<React.HTMLAttributes<HTMLSpanElement>, 'className'> {
  variant: BadgeVariant;
  /** 不給的話直接顯示 variant 原字串——未知值不會消失,也不會讓畫面崩掉。 */
  label?: string | undefined;
  className?: string;
}

/** 前台卡片標籤。未知值一律容忍，退回顯示原始字串，不當成錯誤。 */
export function Badge({ variant, label, className, ...rest }: BadgeProps) {
  const style = (() => {
    switch (variant) {
      case 'New':
        return 'border border-info/20 bg-info-subtle text-info';
      case 'Popular':
        return 'border border-primary/20 bg-primary-subtle text-primary-text';
      case 'LastCall':
        return 'border border-warning/20 bg-warning-subtle text-warning-text';
      default:
        return 'border border-border-soft bg-surface-sunken text-fg-muted';
    }
  })();

  return (
    <span
      className={cn(
        'inline-flex items-center rounded-pill px-[var(--gg-space-3)] py-[var(--gg-space-1)]',
        'text-[length:var(--gg-text-xs)] font-bold',
        style,
        className,
      )}
      {...rest}
    >
      {label ?? variant}
    </span>
  );
}
