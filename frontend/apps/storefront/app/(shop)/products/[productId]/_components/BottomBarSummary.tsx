import { PriceDisplay } from '@greygray/ui';
import { formatMoney, type Money } from '@greygray/api-client';
import { subtotalPreview } from '../../../../_lib/subtotalPreview';

export interface BottomBarSummaryProps {
  /** 目前選的數量（≥ 1）。 */
  quantity: number;
  /** 選定 SKU 的單價。沒有定價時傳 `null`——這一塊整個留白，理由由上方的 `disabledReason` 說。 */
  price: Money | null | undefined;
  /** 「NT$60／32 顆」這種包裝單價，SKU 有 `unitOfMeasure` ＋ `unitCount` 才有。 */
  unitPriceLabel: string | null;
}

/**
 * 底部列左側：「N 件 · 單價」＋「小計 NT$X」。
 *
 * **純呈現，沒有 state**——`AddToCartPanel` 把 `quantity` 傳進來就好。
 * 這樣切是因為這個 workspace 沒有 jsdom：`useState` 的變化測不到，
 * 但「quantity=5、單價 NT$60 → 畫出 NT$300」用 `renderToStaticMarkup` 驗得到。
 *
 * 小計是 ADR-033 的顯示用預覽，**不是帳**——見 `subtotalPreview` 的檔頭。
 */
export function BottomBarSummary({ quantity, price, unitPriceLabel }: BottomBarSummaryProps) {
  if (!price) {
    return <div className="flex flex-col" />;
  }

  return (
    <div className="flex flex-col">
      {/* 唸出來是「5 件 · NT$60　小計 NT$300」，順序本身就是句子，不另外加 aria-label 重複一次。 */}
      <span className="text-[length:var(--gg-text-xs)] text-fg-muted">
        {quantity} 件 · {unitPriceLabel ?? formatMoney(price)}
      </span>
      <span className="inline-flex items-baseline gap-[var(--gg-space-2)]">
        <span className="text-[length:var(--gg-text-xs)] text-fg-muted">小計</span>
        <PriceDisplay amount={subtotalPreview(price, quantity)} size="lg" />
      </span>
    </div>
  );
}
