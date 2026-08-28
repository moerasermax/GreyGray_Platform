import { cn } from './internal/cn';
import { formatMoney, type Money } from '@greygray/api-client';

export type PriceDisplaySize = 'sm' | 'md' | 'lg';

const SIZE_CLASS: Record<PriceDisplaySize, string> = {
  sm: 'text-[length:var(--gg-text-sm)]',
  md: 'text-[length:var(--gg-text-lg)]',
  lg: 'text-[length:var(--gg-text-2xl)]',
};

export interface PriceDisplayProps {
  amount: Money;
  /** 原價。只在幣別相同且比現價高時才顯示刪除線，不做任何金額運算，純比大小。 */
  compareAtAmount?: Money | undefined;
  size?: PriceDisplaySize;
  showDecimals?: boolean;
  className?: string;
}

/** 唯一允許格式化金額的地方。呼叫端一律傳 `Money`，不要自己組字串。 */
export function PriceDisplay({
  amount,
  compareAtAmount,
  size = 'md',
  showDecimals,
  className,
}: PriceDisplayProps) {
  const showCompareAt =
    compareAtAmount != null &&
    compareAtAmount.currency === amount.currency &&
    compareAtAmount.amountMinor > amount.amountMinor;

  return (
    <span className={cn('inline-flex items-baseline gap-[var(--gg-space-2)]', className)}>
      <span className={cn('font-display font-extrabold text-primary-text', SIZE_CLASS[size])}>
        {formatMoney(amount, { showDecimals })}
      </span>
      {showCompareAt && (
        <span className="text-[length:var(--gg-text-sm)] text-fg-muted line-through">
          {formatMoney(compareAtAmount, { showDecimals })}
        </span>
      )}
    </span>
  );
}
