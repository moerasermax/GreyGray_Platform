/**
 * 採購清單（M1b-1）的端點呼叫。`packages/api-client/src/endpoints/admin.ts` 檔頭明講
 * 「只涵蓋 M1a，procurement 這一波先不包」，所以這兩支函式放在這裡，不加進那個共用檔。
 *
 * 寫法照抄 `endpoints/admin.ts` 的慣例：只負責把路徑、query、body 組對，
 * 不做角色判斷——`x-required-role` 只決定選單顯不顯示，真正的檢查在後端。
 */
import type { ApiClient } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];
type QueryRecord = Record<string, string | number | boolean | undefined | null>;

export interface ListPurchaseItemsQuery {
  readonly status?: S['PurchaseItemStatus'];
}

export interface ReportPurchasedRequest {
  readonly quantityPurchased: number;
  /** 當地幣別的實付金額。資訊欄位，不產生分錄。 */
  readonly actualPaidOriginal: S['Money'];
  /** 記帳幣（TWD）的實付金額，這個才入帳。 */
  readonly actualPaidBooking: S['Money'];
}

export interface MutationOptions {
  readonly idempotencyKey: string;
  readonly signal?: AbortSignal;
}

export function listPurchaseItems(
  client: ApiClient,
  campaignId: string,
  query: ListPurchaseItemsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<S['PurchaseItem'][]> {
  return client.get(`/v1/campaigns/${campaignId}/purchase-items`, { query: query as QueryRecord, ...options });
}

export function reportPurchased(
  client: ApiClient,
  purchaseItemId: string,
  body: ReportPurchasedRequest,
  options: MutationOptions,
): Promise<void> {
  return client.post(`/v1/purchase-items/${purchaseItemId}/purchased`, { body, ...options });
}
