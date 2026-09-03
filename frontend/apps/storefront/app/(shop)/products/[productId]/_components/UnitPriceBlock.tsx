import { PriceDisplay } from '@greygray/ui';
import type { Money } from '@greygray/api-client';

export interface UnitPriceBlockProps {
  /** 選定 SKU 的單價；`null` 代表這個規格還沒定價。 */
  price: Money | null | undefined;
  /** 「NT$60／32 顆」這種包裝單價，有才顯示。 */
  unitPriceLabel: string | null;
  /** 多規格時顯示已選規格名；單一規格傳 `null`。 */
  variantLabel: string | null;
  /**
   * 沒有價格時要說的那一句。**文案由呼叫端決定、不寫死在這裡**——
   * 它同時是「不能加入購物車」的唯一說明（`AddToCartPanel` 的 `disabledReason`
   * 在沒價格時回 `null`），兩處各寫一份就會變成同一畫面上兩種說法。
   */
  noPriceMessage: string;
}

/**
 * 商品資訊區的單價（面板最上方、規格 chips 之上）。
 *
 * 為什麼不放在 `page.tsx` 的 SSR `<header>`：那一層不知道使用者選了哪個規格，
 * 單價是跟著 SKU 走的。視覺上它緊接在 `<header>` 後面，讀起來仍是同一區。
 *
 * 純呈現元件，理由同 `BottomBarSummary`。
 */
export function UnitPriceBlock({ price, unitPriceLabel, variantLabel, noPriceMessage }: UnitPriceBlockProps) {
  if (!price) {
    return <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{noPriceMessage}</p>;
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-1)]">
      <span className="inline-flex items-baseline gap-[var(--gg-space-2)]">
        <span className="text-[length:var(--gg-text-xs)] text-fg-muted">單價</span>
        {/* 底部列用 lg，這裡小一號用 md（`PriceDisplay` 只有 sm／md／lg 三級）。 */}
        <PriceDisplay amount={price} size="md" />
      </span>
      {(unitPriceLabel || variantLabel) && (
        <span className="text-[length:var(--gg-text-xs)] text-fg-muted">
          {[variantLabel, unitPriceLabel].filter(Boolean).join(' · ')}
        </span>
      )}
    </div>
  );
}
