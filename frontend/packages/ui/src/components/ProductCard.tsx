import { cn } from './internal/cn';
import { Badge, type BadgeVariant } from './Badge';
import { FavoriteHeart } from './FavoriteHeart';
import { PriceDisplay } from './PriceDisplay';
import { Thumbnail } from './Thumbnail';
import type { Money } from '@greygray/api-client';

export interface ProductCardProps {
  /**
   * 商品圖。**直接傳契約的 `Product.imageUrl`，可以是 `null`。**
   * 沒有圖的時候由這個元件畫佔位版面，呼叫端不要自己組 data URI——
   * 那不只是多一份程式碼，`next/image` 也不吃 `data:`。
   */
  imageSrc?: string | null | undefined;
  imageAlt: string;
  name: string;
  /** 1–2 句描述。 */
  description?: string | undefined;
  price: Money;
  compareAtPrice?: Money | undefined;
  /**
   * 單位價格顯示字串，**直接用後端給的 `Product.unitPriceLabel`**（契約 §Product）。
   * 例如「NT$780／32 顆」。不要在前端自己組——「／32 顆」要知道 SKU 的包裝數量，
   * 那是後端才有的資料。這一欄是既有的例外，不牴觸「不做金額運算」那條鐵則。
   */
  unitPriceLabel?: string | undefined;
  badges?: Array<{ variant: BadgeVariant; label?: string | undefined }> | undefined;
  favorited?: boolean;
  onToggleFavorite?: (() => void) | undefined;
  onClick?: (() => void) | undefined;
  className?: string;
}

/**
 * 純展示元件，不處理路由——要包 `next/link` 或 `onClick` 導頁是頁面自己的事。
 * 圖片走 `next/image` ＋ 1:1 裁切（鐵則 7）；`imageUrl` 是 `null` 時不發圖片請求，
 * 直接畫底色佔位。
 */
export function ProductCard({
  imageSrc,
  imageAlt,
  name,
  description,
  price,
  compareAtPrice,
  unitPriceLabel,
  badges,
  favorited = false,
  onToggleFavorite,
  onClick,
  className,
}: ProductCardProps) {
  // 卡片裡還有 FavoriteHeart 這個真正的 <button>，Root 不能也用 <button>——
  // <button> 不可以巢狀，會直接讓 hydration 失敗。可點時改用 role="button" 的 <div>。
  const Root = onClick ? 'div' : 'article';

  function handleKeyDown(event: React.KeyboardEvent) {
    if (!onClick) return;
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onClick();
    }
  }

  return (
    <Root
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      onClick={onClick}
      onKeyDown={onClick ? handleKeyDown : undefined}
      className={cn(
        'flex w-full flex-col overflow-hidden rounded-card bg-surface text-left shadow-card',
        'transition-transform duration-[var(--gg-duration-base)] ease-out-soft',
        onClick && 'hover:-translate-y-0.5',
        className,
      )}
    >
      <div className="relative aspect-square w-full overflow-hidden bg-surface-sunken">
        {/* 卡片在手機兩欄、平板三欄、桌機四欄，讓瀏覽器挑尺寸而不是一律載大圖 */}
        <Thumbnail
          src={imageSrc}
          alt={imageAlt}
          sizes="(max-width: 640px) 50vw, (max-width: 1024px) 33vw, 25vw"
        />

        {badges && badges.length > 0 && (
          <div className="absolute left-[var(--gg-space-2)] top-[var(--gg-space-2)] flex flex-wrap gap-[var(--gg-space-1)]">
            {badges.map((badge, index) => (
              <Badge key={`${badge.variant}-${index}`} variant={badge.variant} label={badge.label} />
            ))}
          </div>
        )}

        {onToggleFavorite && (
          <span
            className="absolute right-[var(--gg-space-2)] top-[var(--gg-space-2)]"
            onClick={(event) => event.stopPropagation()}
          >
            <FavoriteHeart
              pressed={favorited}
              onToggle={onToggleFavorite}
              aria-label={favorited ? `取消收藏 ${name}` : `加入收藏 ${name}`}
            />
          </span>
        )}
      </div>

      <div className="flex flex-1 flex-col gap-[var(--gg-space-1)] p-[var(--gg-space-4)]">
        <h3 className="line-clamp-1 font-display text-[length:var(--gg-text-base)] font-bold text-fg">
          {name}
        </h3>
        {description && (
          <p className="line-clamp-2 text-[length:var(--gg-text-sm)] text-fg-muted">
            {description}
          </p>
        )}

        <div className="mt-auto flex items-end justify-between pt-[var(--gg-space-2)]">
          <div className="flex flex-col gap-[var(--gg-space-1)]">
            <PriceDisplay amount={price} compareAtAmount={compareAtPrice} />
            {unitPriceLabel && (
              <span className="text-[length:var(--gg-text-xs)] text-fg-muted">
                {unitPriceLabel}
              </span>
            )}
          </div>
        </div>
      </div>
    </Root>
  );
}
