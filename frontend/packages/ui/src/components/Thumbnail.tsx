import Image from 'next/image';
import { cn } from './internal/cn';

export interface ThumbnailProps {
  /**
   * 圖片網址。**直接傳契約的 `imageUrl` / `coverImageUrl`，可以是 `null`。**
   * M1a 的 fixture 全部是 `null`，真圖之後走 Cloudflare R2（已在 next.config 白名單）。
   */
  src?: string | null | undefined;
  alt: string;
  /**
   * 給瀏覽器挑尺寸用。`fill` 模式下不給會整頁載大圖，行動裝置特別吃虧。
   * 例：卡片牆 `(max-width: 640px) 50vw, 25vw`、固定小縮圖 `64px`。
   */
  sizes: string;
  /** 首屏的大圖（例如 banner）設 true，其餘一律 lazy。 */
  priority?: boolean;
  className?: string;
}

/**
 * 1:1 以外的裁切由外層容器決定，這裡只負責「有圖就走 `next/image`、沒圖就不發請求」。
 *
 * ── 為什麼不留 data URI 佔位圖 ──
 * 原本每個呼叫端都寫 `src={x ?? placeholderImage(name)}`，組一張 SVG 的 data URI。
 * 那有三個問題：`next/image` 不吃 `data:`（鐵則 7 就繞不過去）、每個呼叫端各寫一次、
 * 而且 SVG 裡的 `fill` 只能寫死顏色，變成 token 規則的例外。
 * 沒有圖的時候什麼都不畫，外層容器的 `bg-surface-sunken` 自己就是佔位。
 */
export function Thumbnail({ src, alt, sizes, priority = false, className }: ThumbnailProps) {
  if (!src) return null;
  return (
    <Image
      src={src}
      alt={alt}
      fill
      sizes={sizes}
      priority={priority}
      className={cn('object-cover', className)}
    />
  );
}
