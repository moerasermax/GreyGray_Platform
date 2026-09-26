import { cn } from './internal/cn';
import { IconButton } from './IconButton';
import { IconSearch, IconX } from './icons';

export interface SearchBarProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit?: (value: string) => void;
  placeholder?: string;
  'aria-label'?: string;
  className?: string;
}

/** 圓角搜尋列（ADR-009 切版重點）。完全受控，沒有內部 state，不需要 'use client'。 */
export function SearchBar({
  value,
  onChange,
  onSubmit,
  placeholder = '搜尋商品',
  className,
  ...rest
}: SearchBarProps) {
  return (
    <form
      role="search"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit?.(value);
      }}
      className={cn(
        'flex items-center gap-[var(--gg-space-2)] rounded-pill border border-border-soft',
        'bg-surface px-[var(--gg-space-4)] py-[var(--gg-space-2)] shadow-card',
        'focus-within:border-primary-strong',
        className,
      )}
    >
      <IconSearch className="text-fg-muted" />
      <input
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        aria-label={rest['aria-label'] ?? placeholder}
        className="min-w-0 flex-1 bg-transparent text-[length:var(--gg-text-base)] text-fg placeholder:text-fg-muted"
      />
      {value.length > 0 && (
        <IconButton
          type="button"
          icon={<IconX />}
          aria-label="清除搜尋文字"
          size="sm"
          onClick={() => onChange('')}
        />
      )}
    </form>
  );
}
