/**
 * 開團狀態的中文標籤與 `StatusPill` tone。`switch` 一律有 `default`：
 * 後端新增列舉值不算破壞性變更，畫面要退回顯示原始字串而不是崩掉（`docs/06-前端工作包.md` 鐵則 5）。
 */
import type { StatusTone } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export function campaignStatusLabel(status: S['CampaignStatus'] | (string & {})): string {
  switch (status) {
    case 'Draft':
      return '草稿';
    case 'Open':
      return '收單中';
    case 'Closed':
      return '已截團';
    case 'TripInProgress':
      return '旅程中';
    case 'Returned':
      return '已返程';
    case 'Settled':
      return '已結團';
    case 'Cancelled':
      return '已取消';
    default:
      return status;
  }
}

export function campaignStatusTone(status: S['CampaignStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'Draft':
      return 'neutral';
    case 'Open':
      return 'success';
    case 'Closed':
      return 'warning';
    case 'TripInProgress':
    case 'Returned':
      return 'info';
    case 'Settled':
      return 'success';
    case 'Cancelled':
      return 'danger';
    default:
      return 'neutral';
  }
}
