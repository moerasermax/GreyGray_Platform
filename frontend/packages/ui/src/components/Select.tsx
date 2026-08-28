import { cn } from './internal/cn';
import { IconChevronDown } from './icons';

export interface SelectProps
  extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'className'> {
  invalid?: boolean;
  className?: string;
  ref?: React.Ref<HTMLSelectElement>;
}

export function Select({ invalid, className, ref, children, ...rest }: SelectProps) {
  return (
    <div className="relative">
      <select
        ref={ref}
        aria-invalid={invalid || undefined}
        className={cn(
          'w-full appearance-none rounded-[var(--gg-radius-sm)] border bg-surface',
          'px-[var(--gg-space-4)] py-[var(--gg-space-2)] pr-[var(--gg-space-7)]',
          'text-[length:var(--gg-text-base)] text-fg',
          'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft',
          'disabled:opacity-50',
          invalid ? 'border-danger' : 'border-border-soft focus:border-primary',
          className,
        )}
        {...rest}
      >
        {children}
      </select>
      <IconChevronDown className="pointer-events-none absolute right-[var(--gg-space-3)] top-1/2 -translate-y-1/2 text-fg-muted" />
    </div>
  );
}
