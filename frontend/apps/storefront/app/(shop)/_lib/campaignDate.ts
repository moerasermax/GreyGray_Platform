/**
 * 開團的出發／回程只是日期顯示（`Format: date`），不牽涉金額，純格式化不算業務規則。
 */
export function formatDateRange(departAt: string, returnAt: string): string {
  const formatter = new Intl.DateTimeFormat('zh-TW', { month: 'long', day: 'numeric' });
  return `${formatter.format(new Date(departAt))} － ${formatter.format(new Date(returnAt))}`;
}
