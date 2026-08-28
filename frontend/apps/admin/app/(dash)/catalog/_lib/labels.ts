/**
 * 商品相關 enum 的中文標籤。`switch` 一律有 `default`：
 * 後端新增列舉值不算破壞性變更，畫面要退回顯示原始字串而不是崩掉（`docs/06-前端工作包.md` 鐵則 5）。
 */
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export function fulfillmentModeLabel(mode: S['FulfillmentMode'] | (string & {})): string {
  switch (mode) {
    case 'Stock':
      return '現貨';
    case 'Preorder':
      return '預購';
    default:
      return mode;
  }
}
