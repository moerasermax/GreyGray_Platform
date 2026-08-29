/**
 * 採購項目狀態的中文標籤與 `StatusPill` tone。
 *
 * `switch` 一律有 `default`：後端新增列舉值不算破壞性變更，畫面要退回顯示原始字串
 * 而不是崩掉（`docs/06-前端工作包.md` 鐵則 5）。
 */
import type { StatusTone } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export function purchaseItemStatusLabel(status: S['PurchaseItemStatus'] | (string & {})): string {
  switch (status) {
    case 'Pending':
      return '待買';
    case 'Purchased':
      return '已買到';
    case 'PriceChangedPendingConfirmation':
      return '漲價待回覆';
    case 'Unavailable':
      return '缺貨';
    default:
      return status;
  }
}

export function purchaseItemStatusTone(status: S['PurchaseItemStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'Pending':
      return 'warning';
    case 'Purchased':
      return 'success';
    case 'PriceChangedPendingConfirmation':
      return 'info';
    case 'Unavailable':
      return 'danger';
    default:
      return 'neutral';
  }
}
