import { cn } from './internal/cn';
import { IconUser } from './icons';

export type AvatarSize = 'sm' | 'md' | 'lg';

const SIZE_CLASS: Record<AvatarSize, string> = {
  sm: 'h-[var(--gg-space-6)] w-[var(--gg-space-6)] text-[length:var(--gg-text-sm)]',
  md: 'h-[var(--gg-touch-min)] w-[var(--gg-touch-min)] text-[length:var(--gg-text-lg)]',
  lg: 'h-[var(--gg-space-8)] w-[var(--gg-space-8)] text-[length:var(--gg-text-2xl)]',
};

export interface AvatarProps {
  src?: string | null | undefined;
  alt: string;
  /** 沒有圖時用來取字首當 fallback。 */
  name?: string | undefined;
  size?: AvatarSize;
  className?: string;
}

/** 沒有 next/image——這個套件目前無法宣告 next 依賴，見 FE-2 交付說明。 */
export function Avatar({ src, alt, name, size = 'md', className }: AvatarProps) {
  const initials = name?.trim().slice(0, 1).toUpperCase();

  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-pill',
        'bg-surface-sunken font-display font-bold text-primary-text',
        SIZE_CLASS[size],
        className,
      )}
    >
      {src ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={src} alt={alt} className="h-full w-full object-cover" />
      ) : initials ? (
        <span aria-hidden={alt ? undefined : true}>{initials}</span>
      ) : (
        <IconUser aria-hidden />
      )}
    </span>
  );
}
