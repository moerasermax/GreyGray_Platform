'use client';

/*
 * 結帳頁「訂購明細」（FE-46）。**純展示**：只讀頁面已經載入的 `cart` 與 `quote`，
 * 不打 API、不算任何金額——列金額用 `CartLine.lineTotal`、商品小計用 `Cart.goodsTotal`／
 * `QuoteResult.goodsTotal`、運費與應付總額用 `QuoteResult`，全部是後端算好的欄位
 * （ADR-033：購物車、結帳、訂單頁的金額一律來自後端）。
 *
 * 唯一在前端做的「加總」是**件數**（quantity 相加），那是整數計數不是金額。
 *
 * 版面：
 * - `lg` 以上由 `checkout/page.tsx` 放在右欄 sticky，全部品項一次列出。
 * - `lg` 以下放在表單卡片之前；品項超過 `ORDER_SUMMARY_VISIBLE_LINES` 列時，
 *   後面的列收進原生 `<details>`（不寫 JS accordion），摘要文字是「顯示全部 N 件」。
 *   折疊那一段用 `lg:hidden`，桌面另有一份 `hidden lg:flex` 的完整列表——
 *   兩份互斥（display:none 不進可讀樹），沒有一個尺寸會同時看到兩份。
 */

import { Badge, PriceDisplay, Thumbnail } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { ExplainDisclosure } from './ExplainDisclosure';

type S = components['schemas'];

/** 手機上預設展開的品項列數；超過就折進 `<details>`。 */
export const ORDER_SUMMARY_VISIBLE_LINES = 3;

export interface OrderSummaryFold<T> {
  /** 一直顯示的前幾列。 */
  visible: T[];
  /** 折進 `<details>` 的其餘列；長度 0 代表不需要折疊。 */
  folded: T[];
}

/**
 * 把品項切成「一直顯示」與「折疊」兩段。
 * 剛好等於 `limit` 列時不折——折一個只有 0 列的 details 沒有意義。
 */
export function foldOrderSummaryLines<T>(lines: readonly T[], limit = ORDER_SUMMARY_VISIBLE_LINES): OrderSummaryFold<T> {
  if (lines.length <= limit) return { visible: [...lines], folded: [] };
  return { visible: lines.slice(0, limit), folded: lines.slice(limit) };
}

/** 件數合計：各列 `quantity` 相加。整數計數，不是金額。 */
export function countOrderSummaryItems(lines: readonly Pick<S['CartLine'], 'quantity'>[]): number {
  return lines.reduce((sum, line) => sum + line.quantity, 0);
}

export interface CheckoutOrderSummaryProps {
  cart: S['Cart'];
  /** 詢價完成才有；沒有時只列品項、件數與商品小計，運費與總額那兩列不畫。 */
  quote: S['QuoteResult'] | null;
  className?: string;
}

function SummaryLine({ line }: { line: S['CartLine'] }) {
  return (
    <li className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-x-[var(--gg-space-3)] py-[var(--gg-space-3)] first:pt-0 last:pb-0">
      <span className="relative flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface-sunken">
        {/* 同 `CartLineRow`：沒圖時 `Thumbnail` 不畫東西，補中性標示、不捏造商品圖。 */}
        {!line.imageUrl && (
          <span aria-hidden className="text-[length:var(--gg-text-xs)] text-fg-muted">
            無圖
          </span>
        )}
        <Thumbnail src={line.imageUrl} alt="" sizes="48px" />
      </span>

      <div className="flex min-w-0 flex-col gap-[var(--gg-space-1)]">
        <p className="line-clamp-2 break-words text-[length:var(--gg-text-sm)] font-bold text-fg">{line.name}</p>
        {line.variantName && (
          <p className="line-clamp-1 break-words text-[length:var(--gg-text-xs)] text-fg-muted">{line.variantName}</p>
        )}
        <p className="flex flex-wrap items-center gap-[var(--gg-space-2)] text-[length:var(--gg-text-xs)] text-fg-muted">
          <Badge variant={line.mode} label={line.mode === 'Stock' ? '現貨' : '預購'} />
          <span className="whitespace-nowrap">× {line.quantity}</span>
        </p>
      </div>

      {/* 金額欄 `shrink-0`＋`whitespace-nowrap`：大金額（NT$1,234,567）不折行、不被長商品名擠扁。 */}
      <span className="shrink-0 whitespace-nowrap text-right">
        <PriceDisplay amount={line.lineTotal} size="sm" />
      </span>
    </li>
  );
}

function SummaryLineList({ lines, className }: { lines: S['CartLine'][]; className?: string }) {
  return (
    <ul className={['flex flex-col divide-y divide-border-soft', className].filter(Boolean).join(' ')}>
      {lines.map((line) => (
        <SummaryLine key={line.id} line={line} />
      ))}
    </ul>
  );
}

export function CheckoutOrderSummary({ cart, quote, className }: CheckoutOrderSummaryProps) {
  const { visible, folded } = foldOrderSummaryLines(cart.lines);
  const itemCount = countOrderSummaryItems(cart.lines);
  // 詢價回來後兩邊的商品小計是同一個數字；沒詢價時只有購物車那份。
  const goodsTotal = quote?.goodsTotal ?? cart.goodsTotal;

  return (
    <section
      aria-labelledby="checkout-order-summary-title"
      className={[
        'flex flex-col gap-[var(--gg-space-3)] rounded-card border border-border-soft bg-surface p-[var(--gg-space-4)]',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <h2 id="checkout-order-summary-title" className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">
        訂購明細
      </h2>

      <SummaryLineList lines={visible} />

      {folded.length > 0 && (
        <>
          {/* 手機：其餘列折進原生 details。 */}
          <details className="group border-t border-border-soft pt-[var(--gg-space-3)] lg:hidden">
            <summary className="cursor-pointer list-none text-[length:var(--gg-text-sm)] font-bold text-primary-text [&::-webkit-details-marker]:hidden">
              <span className="group-open:hidden">顯示全部 {itemCount} 件</span>
              <span className="hidden group-open:inline">收合明細</span>
            </summary>
            <SummaryLineList lines={folded} className="pt-[var(--gg-space-3)]" />
          </details>
          {/* 桌面：右欄一次列完，不折。 */}
          <SummaryLineList lines={folded} className="hidden border-t border-border-soft pt-[var(--gg-space-3)] lg:flex" />
        </>
      )}

      <dl className="flex flex-col gap-[var(--gg-space-2)] border-t border-border-soft pt-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] text-fg-muted">
        <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
          <dt>件數合計</dt>
          <dd className="whitespace-nowrap">{itemCount} 件</dd>
        </div>
        <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
          <dt>商品小計</dt>
          <dd className="whitespace-nowrap">
            <PriceDisplay amount={goodsTotal} size="sm" />
          </dd>
        </div>
        {quote ? (
          <>
            <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
              <dt>運費</dt>
              <dd className="whitespace-nowrap">
                <PriceDisplay amount={quote.shippingFee} size="sm" />
              </dd>
            </div>
            <div className="flex items-center justify-between gap-[var(--gg-space-3)] pt-[var(--gg-space-1)]">
              <dt className="font-bold text-fg">應付總額</dt>
              <dd className="whitespace-nowrap">
                <PriceDisplay amount={quote.grandTotal} size="md" />
              </dd>
            </div>
          </>
        ) : (
          <div className="flex items-center justify-between gap-[var(--gg-space-3)]">
            <dt>運費</dt>
            <dd className="whitespace-nowrap">選擇配送方式後計算</dd>
          </div>
        )}
      </dl>

      {quote && <ExplainDisclosure items={quote.explain} />}
    </section>
  );
}
