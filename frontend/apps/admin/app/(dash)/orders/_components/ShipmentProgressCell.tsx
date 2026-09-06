/**
 * 訂單列表「出貨進度」欄（#49）：張數、簽收進度由 `shipmentProgressText` 算，
 * 這裡只負責畫成 `<td>`。
 */
import type { components } from '@greygray/api-client/admin';
import { shipmentProgressText } from '../_lib/shipmentProgress';

type S = components['schemas'];

export interface ShipmentProgressCellProps {
  readonly orderId: string;
  readonly orderStatus: S['OrderStatus'] | (string & {});
  readonly shipments: readonly S['AdminShipment'][] | null;
  readonly truncated: boolean;
}

export function ShipmentProgressCell({ orderId, orderStatus, shipments, truncated }: ShipmentProgressCellProps) {
  return (
    <td className="px-3 py-2 text-fg-muted">{shipmentProgressText(orderId, orderStatus, shipments, truncated)}</td>
  );
}
