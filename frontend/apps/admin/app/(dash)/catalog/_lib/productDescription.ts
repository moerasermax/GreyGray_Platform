export const PRODUCT_DESCRIPTION_DISPLAY_NOTE = '換行會照樣顯示在商品頁';

export function productDescriptionHint(description: string): string {
  return `目前 ${description.trim().length} 字・${PRODUCT_DESCRIPTION_DISPLAY_NOTE}`;
}
