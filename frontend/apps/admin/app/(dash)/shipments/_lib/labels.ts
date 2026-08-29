/**
 * 出貨單相關 enum 的中文標籤與 `StatusPill` tone。
 *
 * `switch` 一律有 `default`：後端新增列舉值不算破壞性變更，畫面要退回顯示原始字串
 * 而不是崩掉（`docs/06-前端工作包.md` 鐵則 5）。
 */
import type { StatusTone } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export function shipmentStatusLabel(status: S['ShipmentStatus'] | (string & {})): string {
  switch (status) {
    case 'Draft':
      return '草稿';
    case 'Packed':
      return '已打包';
    case 'Dispatched':
      return '已交運';
    case 'InTransit':
      return '運送中';
    case 'ArrivedAtStore':
      return '已到店';
    case 'Delivered':
      return '已送達';
    case 'Returned':
      return '已退回';
    case 'Lost':
      return '已遺失';
    default:
      return status;
  }
}

export function shipmentStatusTone(status: S['ShipmentStatus'] | (string & {})): StatusTone {
  switch (status) {
    case 'Draft':
      return 'neutral';
    case 'Packed':
    case 'Dispatched':
    case 'InTransit':
    case 'ArrivedAtStore':
      return 'info';
    case 'Delivered':
      return 'success';
    case 'Returned':
    case 'Lost':
      return 'danger';
    default:
      return 'neutral';
  }
}

export function shipmentMethodLabel(method: S['DeliveryMethod'] | (string & {})): string {
  switch (method) {
    case 'ConvenienceStore':
      return '超商取貨';
    case 'HomeDelivery':
      return '宅配到府';
    case 'SelfPickup':
      return '自取';
    default:
      return method;
  }
}
