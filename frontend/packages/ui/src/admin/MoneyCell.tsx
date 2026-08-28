/**
 * 金額欄位。tabular-nums、右對齊，禁止在表格裡直接印金額字串。
 *
 * 這裡**不呼叫 `formatMoney`**——`packages/ui` 沒有依賴 `@greygray/api-client`，
 * 呼叫端要先用 `formatMoney(money, { showDecimals })` 把 `Money` 轉成字串再傳進來。
 * 帳務畫面（分錄、對帳）記得傳 `showDecimals: true`，一分錢的差異就是要查的東西。
 */
export interface MoneyCellProps {
  /** 已經用 `formatMoney` 格式化好的字串，例如 `"NT$1,280,000"`。 */
  readonly value: string;
  readonly className?: string;
  /** 金額是負的（例如退款）時可以額外標紅，不影響數字本身。 */
  readonly negative?: boolean;
}

export function MoneyCell({ value, className, negative }: MoneyCellProps) {
  return (
    <td
      data-numeric
      className={`gg-numeric px-3 py-2 ${negative ? 'text-danger' : ''} ${className ?? ''}`.trim()}
    >
      {value}
    </td>
  );
}
