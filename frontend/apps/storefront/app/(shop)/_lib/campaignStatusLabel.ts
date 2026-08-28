import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

/** 顯示用的中文標籤。**一定要有 `default`**——後端新增狀態不算破壞性變更。 */
export function campaignStatusLabel(status: S['CampaignStatus'] | (string & {})): string {
  switch (status) {
    case 'Draft':
      return '草稿';
    case 'Open':
      return '開團中';
    case 'Closed':
      return '已截團';
    case 'TripInProgress':
      return '旅程進行中';
    case 'Returned':
      return '已回國';
    case 'Settled':
      return '已結算';
    case 'Cancelled':
      return '已取消';
    default:
      return status;
  }
}
