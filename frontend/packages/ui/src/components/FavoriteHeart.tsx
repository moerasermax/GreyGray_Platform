import { cn } from './internal/cn';
import { IconHeart } from './icons';

export interface FavoriteHeartProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'onClick'> {
  pressed: boolean;
  onToggle: () => void;
  /** 預設「加入收藏」／「取消收藏」，有更貼切的商品名稱時可覆蓋。 */
  'aria-label'?: string;
  className?: string;
  ref?: React.Ref<HTMLButtonElement>;
}

/** 完全受控——狀態與切換都由呼叫端決定，元件本身不猜業務規則。 */
export function FavoriteHeart({
  pressed,
  onToggle,
  className,
  ref,
  ...rest
}: FavoriteHeartProps) {
  const ariaLabel = rest['aria-label'] ?? (pressed ? '取消收藏' : '加入收藏');

  return (
    <button
      ref={ref}
      type="button"
      aria-pressed={pressed}
      aria-label={ariaLabel}
      onClick={onToggle}
      className={cn(
        // self-start：單獨的方形圖示鈕常被直接丟進 flex-col 容器，預設 align-items: stretch
        // 會把它撐成整欄寬、aspect-square 再讓高度跟著暴衝，變成一個巨大的圓。
        'inline-flex aspect-square shrink-0 self-start items-center justify-center rounded-pill',
        'bg-surface/90 text-[length:var(--gg-text-lg)] shadow-card backdrop-blur-sm',
        'p-[var(--gg-space-2)] transition-colors duration-[var(--gg-duration-base)] ease-out-soft',
        pressed ? 'text-primary' : 'text-fg-muted hover:text-primary-text',
        className,
      )}
      {...rest}
    >
      <IconHeart filled={pressed} decorative />
    </button>
  );
}
