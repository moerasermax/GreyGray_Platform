import type { BadgeVariant } from '@greygray/ui';

/**
 * `ProductListItem.badges` 是後端給的原始字串陣列，`Badge` 元件本身就容忍未知值
 * （顯示原字串），這裡只是把陣列轉成 `ProductCard` 要的形狀，不做任何篩選或轉譯。
 */
export function toBadgeProps(badges: readonly string[]): Array<{ variant: BadgeVariant }> {
  return badges.map((variant) => ({ variant }));
}
