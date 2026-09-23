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
  // 沒有圖時用分類名稱的第一個字當文字圖形——不猜分類是什麼業務、也不留空圓。
  // `Array.from` 才不會把代理對（emoji、罕用字）切成半個。
  const glyph = Array.from(label.trim())[0] ?? '';

  return (
    <button
      type="button"
      aria-pressed={onClick ? selected : undefined}
      onClick={onClick}
      className={cn(
        'group flex shrink-0 snap-start flex-col items-center gap-[var(--gg-space-2)]',
        'w-[var(--gg-space-8)] text-center focus-visible:outline-none',
        className,
      )}
    >
      <span
        className={cn(
          'relative flex aspect-square w-full items-center justify-center overflow-hidden',
          'rounded-pill border border-border-soft bg-surface-sunken',
          'transition-colors duration-[var(--gg-duration-base)] ease-out-soft',
          'group-focus-visible:ring-2 group-focus-visible:ring-primary group-focus-visible:ring-offset-2',
          selected && 'border-primary ring-2 ring-primary',
        )}
      >
        {imageSrc ? (
          <Thumbnail src={imageSrc} alt={imageAlt} sizes="96px" />
        ) : (
          <span
            aria-hidden={true}
            className={cn(
              'font-display text-[length:var(--gg-text-base)] font-bold leading-none',
              selected ? 'text-primary-text' : 'text-fg-muted',
            )}
          >
            {glyph}
          </span>
        )}
      </span>
      <span
        className={cn(
          'line-clamp-2 break-words text-[length:var(--gg-text-xs)] leading-snug',
          selected ? 'font-bold text-primary-text' : 'text-fg-muted',
        )}
      >
        {label}
      </span>
    </button>
  );
}
