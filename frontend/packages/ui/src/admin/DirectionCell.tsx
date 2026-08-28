/**
 * 借／貸方向。**顏色之外一定要有文字**——色覺障礙的使用者不能只靠顏色分辨。
 *
 * `data-direction` 屬性驅動的顏色規則已經寫在 `apps/admin/app/globals.css`
 * （`[data-direction='Debit']` / `[data-direction='Credit']`），這裡不重複定義顏色。
 */
export type Direction = 'Debit' | 'Credit';

export interface DirectionCellProps {
  /** enum 容忍未知值：後端新增方向不算破壞性變更，畫面顯示原始字串而不是崩掉。 */
  readonly direction: Direction | (string & {});
  readonly className?: string;
}

const LABEL: Record<Direction, string> = {
  Debit: '借',
  Credit: '貸',
};

export function DirectionCell({ direction, className }: DirectionCellProps) {
  const known = direction === 'Debit' || direction === 'Credit';
  const label = known ? LABEL[direction as Direction] : direction;

  return (
    <td
      data-direction={known ? direction : undefined}
      className={`px-3 py-2 text-sm font-medium ${className ?? ''}`.trim()}
    >
      {label}
    </td>
  );
}
