import { cn } from './internal/cn';

export type SkeletonVariant = 'text' | 'block' | 'circle';

const VARIANT_CLASS: Record<SkeletonVariant, string> = {
  text: 'rounded-sm',
  block: 'rounded-card',
  circle: 'rounded-pill',
};

export interface SkeletonProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: SkeletonVariant;
}

/**
 * 載入中佔位。**呼叫端一定要用 `className`（或 `style`）給出跟真實內容一樣的高度**，
 * 這裡不會自己猜——猜錯的話載入完成那一刻畫面會跳（CLS）。
 */
export function Skeleton({ variant = 'text', className, ...rest }: SkeletonProps) {
  return (
    <div
      aria-hidden
      className={cn('animate-pulse bg-surface-sunken', VARIANT_CLASS[variant], className)}
      {...rest}
    />
  );
}
