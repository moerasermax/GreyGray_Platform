import { cn } from './internal/cn';
import type { ButtonVariant } from './Button';

const VARIANT_CLASS: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-on-primary hover:bg-primary-hover',
  secondary:
    'border border-border-strong bg-surface text-primary-text hover:bg-surface-sunken',
  ghost: 'bg-transparent text-fg hover:bg-surface-sunken',
  danger: 'bg-danger text-on-primary hover:opacity-90',
};

export type IconButtonSize = 'sm' | 'md' | 'lg';

const SIZE_CLASS: Record<IconButtonSize, string> = {
  sm: 'p-[var(--gg-space-2)] text-[length:var(--gg-text-base)]',
  md: 'p-[var(--gg-space-3)] text-[length:var(--gg-text-lg)]',
  lg: 'p-[var(--gg-space-4)] text-[length:var(--gg-text-xl)]',
};

export interface IconButtonProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  icon: React.ReactNode;
  /** 一定要有——這是唯一的可存取名稱來源，圖示本身不能傳達語意。 */
  'aria-label': string;
  variant?: ButtonVariant;
  size?: IconButtonSize;
  className?: string;
  ref?: React.Ref<HTMLButtonElement>;
}

/** 圓形圖示按鈕。全域 CSS 已保證最小 44×44 觸控目標，這裡不用再重複寫死。 */
export function IconButton({
  icon,
  variant = 'ghost',
  size = 'md',
  disabled,
  className,
  ref,
  type = 'button',
  ...rest
}: IconButtonProps) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled}
      className={cn(
        // self-start：跟 FavoriteHeart 同理——單獨的方形按鈕被丟進 flex-col 容器時，
        // 預設 align-items: stretch 會撐成整欄寬，aspect-square 讓高度跟著暴衝。
        'inline-flex aspect-square shrink-0 self-start items-center justify-center rounded-pill',
        'transition-colors duration-[var(--gg-duration-base)] ease-out-soft',
        'disabled:pointer-events-none disabled:opacity-50',
        VARIANT_CLASS[variant],
        SIZE_CLASS[size],
        className,
      )}
      {...rest}
    >
      {icon}
    </button>
  );
}
