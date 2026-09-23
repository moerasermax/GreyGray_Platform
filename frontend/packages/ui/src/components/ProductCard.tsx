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
  /**
   * 未定價商品傳 `null`／不傳：價格區改顯示 `unavailableLabel`，**不補假價格**。
   * 媒體區、標題與卡片節奏跟有定價的卡完全相同。
   */
  price?: Money | null | undefined;
  /** 沒有 `price` 時顯示在價格區的說明（例如「目前無法購買，點擊查看詳情」）。 */
  unavailableLabel?: string | undefined;
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
  /**
   * 呼叫端自己組的收藏鈕，放在跟內建愛心同一個位置（媒體區右上）。
   * 給了就不畫內建的那一顆；導航攔截由呼叫端自己負責。
   */
  favoriteSlot?: React.ReactNode | undefined;
  onClick?: (() => void) | undefined;
  className?: string;
}

/**
 * `suppressCardNavigation` 只用得到事件的這兩個方法。**刻意不是 `React.MouseEvent`**——
 * 這個 workspace 沒有 jsdom 也沒有 `@testing-library`（不加相依是這一包的前提），
 * 型別縮到最小才餵得進假的 event 物件做單元測試。
 * `React.MouseEvent` 兩個方法都有，所以真的事件仍然傳得進來。
 */
export interface SuppressibleCardEvent {
  preventDefault: () => void;
  stopPropagation: () => void;
}

/**
 * 卡片內的互動元素（收藏心）要吃掉點擊，不讓它變成「開啟這張卡」。
 *
 * **兩個都要呼叫，少一個就是「現在卡在哪」#27 那個 bug。**
 * 商品列表把整張卡包在 `<Link>` 裡（`storefront .../ProductCardLink.tsx`），
 * 於是點愛心會走到兩條各自獨立的導航路徑：
 *
 * 1. `<a href>` 的**瀏覽器預設行為** —— 只有 `preventDefault()` 擋得住。
 *    `stopPropagation()` 擋不住它：預設行為是在事件傳播「結束之後」才執行的，
 *    跟還有沒有人在監聽無關。
 * 2. `next/link` 掛在 `<a>` 上的 `onClick`（client-side 導航）—— `stopPropagation()`
 *    讓它收不到事件；就算收到了，它自己也會先看 `defaultPrevented` 而提早 return。
 *
 * 修好前的版本只寫了 `stopPropagation()`，剛好把①漏掉，
 * 結果是「愛心不會切換、整頁跳去商品詳情」。
 *
 * 呼叫時機在 `FavoriteHeart` 自己的 `onToggle` **之後**（事件由內往外冒泡），
 * 所以收藏狀態照常切換，被擋掉的只有導航。
 */
export function suppressCardNavigation(event: SuppressibleCardEvent): void {
  event.preventDefault();
  event.stopPropagation();
}

/**
 * 純展示元件，不處理路由——要包 `next/link` 或 `onClick` 導頁是頁面自己的事。
 * 圖片走 `next/image` ＋ 1:1 裁切（鐵則 7）；`imageUrl` 是 `null` 時不發圖片請求，
 * 畫中性的「商品圖片待補」佔位版面（不捏造商品照片）。
 */
export function ProductCard({
  imageSrc,
  imageAlt,
  name,
  description,
  price,
  unavailableLabel,
  compareAtPrice,
  unitPriceLabel,
  badges,
  favorited = false,
  onToggleFavorite,
  favoriteSlot,
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
      <div className="relative aspect-square w-full overflow-hidden border-b border-border-soft bg-surface-sunken">
        {!imageSrc && <ImagePendingPlaceholder />}
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

        {favoriteSlot && (
          <span className="absolute right-[var(--gg-space-2)] top-[var(--gg-space-2)]">
            {favoriteSlot}
          </span>
        )}

        {!favoriteSlot && onToggleFavorite && (
          <span
            className="absolute right-[var(--gg-space-2)] top-[var(--gg-space-2)]"
            onClick={suppressCardNavigation}
          >
            <FavoriteHeart
              pressed={favorited}
              onToggle={onToggleFavorite}
              aria-label={favorited ? `取消收藏 ${name}` : `加入收藏 ${name}`}
            />
          </span>
        )}
      </div>

      <div className="flex flex-1 flex-col gap-[var(--gg-space-1)] p-[var(--gg-space-3)] sm:p-[var(--gg-space-4)]">
        <h3 className="line-clamp-2 break-words font-display text-[length:var(--gg-text-sm)] font-bold leading-snug text-fg sm:text-[length:var(--gg-text-base)]">
          {name}
        </h3>
        {description && (
          <p className="line-clamp-2 text-[length:var(--gg-text-sm)] text-fg-muted">
            {description}
          </p>
        )}

        <div className="mt-auto flex items-end justify-between pt-[var(--gg-space-2)]">
          <div className="flex flex-col gap-[var(--gg-space-1)]">
            {price ? (
              <PriceDisplay amount={price} compareAtAmount={compareAtPrice} />
            ) : (
              unavailableLabel && (
                <p className="text-[length:var(--gg-text-xs)] text-fg-muted">{unavailableLabel}</p>
              )
            )}
            {price && unitPriceLabel && (
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

/**
 * 沒有商品圖時的佔位版面：通用的「圖片」線條圖示 ＋ 一句說明。
 * 刻意中性——不放任何看起來像商品照片的東西，顏色只用 token（`currentColor`）。
 */
function ImagePendingPlaceholder() {
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-[var(--gg-space-2)] p-[var(--gg-space-3)] text-center text-fg-muted">
      <svg
        aria-hidden={true}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth={1.5}
        strokeLinecap="round"
        strokeLinejoin="round"
        className="h-[var(--gg-space-6)] w-[var(--gg-space-6)]"
      >
        <rect x="3" y="4" width="18" height="16" rx="3" />
        <circle cx="9" cy="10" r="1.5" />
        <path d="M21 16l-4.5-4.5L8 20" />
      </svg>
      <span className="text-[length:var(--gg-text-xs)]">商品圖片待補</span>
    </div>
  );
}
