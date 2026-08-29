/**
 * 出貨單（M1b，`/v1/shipments`）的端點呼叫。`packages/api-client/src/endpoints/admin.ts`
 * 檔頭明講「只涵蓋 M1a，procurement／fulfillment／inventory／trip-costs 這一波先不包」，
 * 所以這幾支函式放在這裡，不加進那個共用檔——這一波 `endpoints/**` 也沒有人擁有，
 * 兩包平行 agent 同時改同一支共用檔正是要避免的衝突（見 `docs/15` §2）。
 *
 * 寫法照抄 `endpoints/admin.ts`／`procurement/_lib/api.ts` 的慣例：只負責把路徑、
 * query、body 組對，不做角色判斷——`x-required-role` 只決定選單顯不顯示，
 * 真正的檢查在後端。
 */
import type { ApiClient, Page } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];
type QueryRecord = Record<string, string | number | boolean | undefined | null>;

export interface ListShipmentsQuery {
  readonly status?: S['ShipmentStatus'];
  readonly cursor?: string;
  readonly limit?: number;
}

export interface CreateShipmentRequest {
  readonly orderIds: readonly string[];
  readonly method: S['DeliveryMethod'];
}

export interface DispatchShipmentRequest {
  readonly trackingNumber: string;
  /** 付給物流商的成本，不是向客人收的運費——後者是訂單的 `shippingFee`。 */
  readonly carrierCost: S['Money'];
}

export interface MutationOptions {
  readonly idempotencyKey: string;
  readonly signal?: AbortSignal;
}

export function listShipments(
  client: ApiClient,
  query: ListShipmentsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['AdminShipment']>> {
  return client.get('/v1/shipments', { query: query as QueryRecord, ...options });
}

export function createShipment(
  client: ApiClient,
  body: CreateShipmentRequest,
  options: MutationOptions,
): Promise<S['AdminShipment']> {
  return client.post('/v1/shipments', { body, ...options });
}

export function dispatchShipment(
  client: ApiClient,
  shipmentId: string,
  body: DispatchShipmentRequest,
  options: MutationOptions,
): Promise<S['AdminShipment']> {
  return client.post(`/v1/shipments/${shipmentId}/dispatch`, { body, ...options });
}

export function deliverShipment(
  client: ApiClient,
  shipmentId: string,
  options: MutationOptions,
): Promise<S['AdminShipment']> {
  return client.post(`/v1/shipments/${shipmentId}/deliver`, { ...options });
}
