import { cn } from './internal/cn';

export interface InputProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'className'> {
  invalid?: boolean;
  className?: string;
  ref?: React.Ref<HTMLInputElement>;
}

export function Input({ invalid, className, ref, ...rest }: InputProps) {
  return (
    <input
      ref={ref}
      aria-invalid={invalid || undefined}
      className={cn(
        'w-full rounded-[var(--gg-radius-sm)] border bg-surface px-[var(--gg-space-4)] py-[var(--gg-space-2)]',
        'text-[length:var(--gg-text-base)] text-fg placeholder:text-fg-muted',
        'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft',
        'disabled:opacity-50',
        invalid ? 'border-danger' : 'border-border-soft focus:border-primary-strong',
        className,
      )}
      {...rest}
    />
  );
}
