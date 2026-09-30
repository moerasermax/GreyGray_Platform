import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export const MIXED_ORDER_SHIP_SEPARATELY =
  '這筆訂單有現貨也有預購：現貨會先寄出，預購商品到貨後另外寄出。';
export const MIXED_ORDER_HOLD_UNTIL_COMPLETE =
  '這筆訂單有現貨也有預購：會等預購商品到齊後一起寄出。';
export const MIXED_ORDER_PICKUP_SEPARATELY =
  '這筆訂單有現貨也有預購：現貨可以先取貨，預購商品到貨後另外通知取貨。';
export const MIXED_ORDER_PICKUP_HOLD_UNTIL_COMPLETE =
  '這筆訂單有現貨也有預購：會等預購商品到齊後一起通知取貨。';
export const COPY_ORDER_NUMBER_IDLE = '複製';
export const COPY_ORDER_NUMBER_SUCCEEDED = '已複製';
export const COPY_ORDER_NUMBER_FAILED = '複製失敗，請手動選取';

const INACTIVE_LINE_STATUSES: ReadonlySet<S['OrderLineStatus']> = new Set(['Unavailable', 'Cancelled']);

export function hasMixedOrderModes(lines: readonly S['OrderLine'][]): boolean {
  let hasStock = false;
  let hasPreorder = false;

  for (const line of lines) {
    if (INACTIVE_LINE_STATUSES.has(line.status)) continue;
    if (line.mode === 'Stock') hasStock = true;
    if (line.mode === 'Preorder') hasPreorder = true;
  }

  return hasStock && hasPreorder;
}

export function mixedOrderShippingMessage(
  order: Pick<S['Order'], 'lines' | 'shippingPolicy' | 'deliveryMethod'>,
): string | null {
  if (!hasMixedOrderModes(order.lines)) return null;
  if (order.deliveryMethod === 'SelfPickup') {
    return order.shippingPolicy === 'HoldUntilComplete'
      ? MIXED_ORDER_PICKUP_HOLD_UNTIL_COMPLETE
      : MIXED_ORDER_PICKUP_SEPARATELY;
  }
  return order.shippingPolicy === 'HoldUntilComplete'
    ? MIXED_ORDER_HOLD_UNTIL_COMPLETE
    : MIXED_ORDER_SHIP_SEPARATELY;
}

export function formatPlacedAtInTaipei(placedAt: string): string {
  return new Intl.DateTimeFormat('zh-TW', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone: 'Asia/Taipei',
  }).format(new Date(placedAt));
}

export async function copyOrderNumber(
  orderNumber: string,
  writeText: ((text: string) => Promise<void>) | undefined,
): Promise<typeof COPY_ORDER_NUMBER_SUCCEEDED | typeof COPY_ORDER_NUMBER_FAILED> {
  if (!writeText) return COPY_ORDER_NUMBER_FAILED;

  try {
    await writeText(orderNumber);
    return COPY_ORDER_NUMBER_SUCCEEDED;
  } catch {
    return COPY_ORDER_NUMBER_FAILED;
  }
}
