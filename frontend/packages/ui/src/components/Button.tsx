import { cn } from './internal/cn';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const VARIANT_CLASS: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-on-primary hover:bg-primary-hover',
  secondary:
    'border border-border-strong bg-surface text-primary-text hover:bg-surface-sunken',
  ghost: 'bg-transparent text-primary-text hover:bg-surface-sunken',
  danger: 'bg-danger text-on-danger hover:opacity-90',
};

const SIZE_CLASS: Record<ButtonSize, string> = {
  sm: 'px-[var(--gg-space-3)] py-[var(--gg-space-1)] text-[length:var(--gg-text-sm)]',
  md: 'px-[var(--gg-space-5)] py-[var(--gg-space-2)] text-[length:var(--gg-text-base)]',
  lg: 'px-[var(--gg-space-6)] py-[var(--gg-space-3)] text-[length:var(--gg-text-lg)]',
};

export interface ButtonProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  fullWidth?: boolean;
  className?: string;
  ref?: React.Ref<HTMLButtonElement>;
}

/** pill 圓角、loading 時自動 disabled。顏色與圓角只從 token 來。 */
export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  fullWidth = false,
  disabled,
  children,
  className,
  ref,
  type = 'button',
  ...rest
}: ButtonProps) {
  const isDisabled = disabled || loading;

  return (
    <button
      ref={ref}
      type={type}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      className={cn(
        'inline-flex items-center justify-center gap-[var(--gg-space-2)] rounded-pill font-display font-bold',
        'transition-colors duration-[var(--gg-duration-base)] ease-out-soft',
        'disabled:pointer-events-none disabled:opacity-50',
        fullWidth && 'w-full',
        VARIANT_CLASS[variant],
        SIZE_CLASS[size],
        className,
      )}
      {...rest}
    >
      {loading && <Spinner aria-hidden />}
      {children}
    </button>
  );
}
