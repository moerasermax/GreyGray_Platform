import { cn } from './internal/cn';
import { Thumbnail } from './Thumbnail';

export interface CategoryChipProps {
  /** 直接傳契約的 `Category.imageUrl`，可以是 `null`——沒圖就由外層底色當佔位。 */
  imageSrc?: string | null | undefined;
  imageAlt: string;
  label: string;
  selected?: boolean;
  onClick?: () => void;
  className?: string;
}

/**
 * 單一分類標籤。橫捲容器（`overflow-x-auto` + `snap-x`）由放它的頁面負責，
 * 這裡只確保自己有 `snap-start` 可以直接被裝進那種容器。
 */
export function CategoryChip({
  imageSrc,
  imageAlt,
  label,
  selected = false,
  onClick,
  className,
}: CategoryChipProps) {
  return (
    <button
      type="button"
      aria-pressed={onClick ? selected : undefined}
      onClick={onClick}
      className={cn(
        'flex shrink-0 snap-start flex-col items-center gap-[var(--gg-space-2)]',
        'w-[var(--gg-space-8)] text-center',
        className,
      )}
    >
      <span
        className={cn(
          'relative aspect-square w-full overflow-hidden rounded-pill bg-surface-sunken',
          'transition-colors duration-[var(--gg-duration-base)] ease-out-soft',
          selected && 'ring-2 ring-primary',
        )}
      >
        <Thumbnail src={imageSrc} alt={imageAlt} sizes="96px" />
      </span>
      <span
        className={cn(
          'line-clamp-1 text-[length:var(--gg-text-xs)]',
          selected ? 'font-bold text-primary-text' : 'text-fg-muted',
        )}
      >
        {label}
      </span>
    </button>
  );
}
