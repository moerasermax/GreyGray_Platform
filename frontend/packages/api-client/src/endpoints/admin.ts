/**
 * Admin BFF（:5001）端點層。**只負責把路徑、query、body 組對，型別接對**，
 * 不做角色判斷——`x-required-role` 只決定選單顯不顯示，真正的檢查在後端。
 *
 * 契約沒有 `operationId`，函式名是這一包自己取的。方法 ＋ 路徑 → 函式名對照表
 * 見 `frontend/packages/api-client/src/endpoints/README.md`。
 *
 * **只涵蓋 M1a。** M1b／M2／M3 的端點（procurement、fulfillment、lots、
 * trip-costs、reporting、audit）契約已定但實作延後，這一包先不包。
 */

import type { ApiClient, Page } from '../http';
import type { components } from '../types.admin';

type S = components['schemas'];
/** `RequestOptions.query` 要求索引簽章；具名 query 介面沒有，這裡轉一手。 */
type QueryRecord = Record<string, string | number | boolean | undefined | null>;

export interface CallOptions {
  readonly idempotencyKey?: string;
  readonly signal?: AbortSignal;
}

export interface AdminLoginRequest {
  readonly email: string;
  readonly password: string;
}

export interface ListAdminProductsQuery {
  readonly q?: string;
  readonly categoryId?: string;
  readonly includeArchived?: boolean;
  readonly cursor?: string;
  readonly limit?: number;
}

export interface AddCampaignOfferRequest {
  readonly skuId: string;
  readonly sellingPrice: S['Money'];
  readonly targetPurchasePrice?: S['Money'] | null;
}

export interface CancelCampaignRequest {
  readonly reason: string;
}

export interface ListAdminCampaignsQuery {
  readonly status?: S['CampaignStatus'];
  readonly cursor?: string;
  readonly limit?: number;
}

export interface ListAdminOrdersQuery {
  readonly q?: string;
  readonly status?: S['OrderStatus'];
  readonly campaignId?: string;
  readonly cursor?: string;
  readonly limit?: number;
}

export interface CancelAdminOrderRequest {
  readonly reason: string;
  readonly refundTo: S['RefundDestination'];
}

export interface ListLedgerEntriesQuery {
  readonly sourceModule?: string;
  readonly sourceRef?: string;
  readonly from?: string;
  readonly to?: string;
  readonly cursor?: string;
  readonly limit?: number;
}

// ── auth ──────────────────────────────────────────────────────────────────

export function login(client: ApiClient, body: AdminLoginRequest, options: CallOptions = {}): Promise<S['Staff']> {
  return client.post('/v1/auth/login', { body, ...options });
}

export function logout(client: ApiClient, options: CallOptions = {}): Promise<void> {
  return client.post('/v1/auth/logout', { ...options });
}

export function getMe(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['Staff']> {
  return client.get('/v1/me', options);
}

// ── catalog ───────────────────────────────────────────────────────────────

export function listCategories(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['Category'][]> {
  return client.get('/v1/categories', options);
}

export function createCategory(
  client: ApiClient,
  body: S['CategoryInput'],
  options: CallOptions = {},
): Promise<S['Category']> {
  return client.post('/v1/categories', { body, ...options });
}

export function updateCategory(
  client: ApiClient,
  categoryId: string,
  body: S['CategoryInput'],
  options: CallOptions = {},
): Promise<S['Category']> {
  return client.patch(`/v1/categories/${categoryId}`, { body, ...options });
}

export function listProducts(
  client: ApiClient,
  query: ListAdminProductsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['AdminProduct']>> {
  return client.get('/v1/products', { query: query as QueryRecord, ...options });
}

export function createProduct(
  client: ApiClient,
  body: S['AdminProductInput'],
  options: CallOptions = {},
): Promise<S['AdminProduct']> {
  return client.post('/v1/products', { body, ...options });
}

export function getProduct(
  client: ApiClient,
  productId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['AdminProduct']> {
  return client.get(`/v1/products/${productId}`, options);
}

export function updateProduct(
  client: ApiClient,
  productId: string,
  body: S['AdminProductInput'],
  options: CallOptions = {},
): Promise<S['AdminProduct']> {
  return client.patch(`/v1/products/${productId}`, { body, ...options });
}

export function updateSku(
  client: ApiClient,
  skuId: string,
  body: S['AdminSkuInput'],
  options: CallOptions = {},
): Promise<S['AdminSku']> {
  return client.patch(`/v1/skus/${skuId}`, { body, ...options });
}

// ── campaign ──────────────────────────────────────────────────────────────

export function listCampaigns(
  client: ApiClient,
  query: ListAdminCampaignsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['AdminCampaign']>> {
  return client.get('/v1/campaigns', { query: query as QueryRecord, ...options });
}

export function createCampaign(
  client: ApiClient,
  body: S['AdminCampaignInput'],
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.post('/v1/campaigns', { body, ...options });
}

export function getCampaign(
  client: ApiClient,
  campaignId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['AdminCampaignDetail']> {
  return client.get(`/v1/campaigns/${campaignId}`, options);
}

export function updateCampaign(
  client: ApiClient,
  campaignId: string,
  body: S['AdminCampaignInput'],
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.patch(`/v1/campaigns/${campaignId}`, { body, ...options });
}

export function publishCampaign(
  client: ApiClient,
  campaignId: string,
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.post(`/v1/campaigns/${campaignId}/publish`, { ...options });
}

export function closeCampaign(
  client: ApiClient,
  campaignId: string,
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.post(`/v1/campaigns/${campaignId}/close`, { ...options });
}

export function cancelCampaign(
  client: ApiClient,
  campaignId: string,
  body: CancelCampaignRequest,
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.post(`/v1/campaigns/${campaignId}/cancel`, { body, ...options });
}

export function settleCampaign(
  client: ApiClient,
  campaignId: string,
  options: CallOptions = {},
): Promise<S['AdminCampaign']> {
  return client.post(`/v1/campaigns/${campaignId}/settle`, { ...options });
}

export function listCampaignOffers(
  client: ApiClient,
  campaignId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['AdminCampaignOffer'][]> {
  return client.get(`/v1/campaigns/${campaignId}/offers`, options);
}

export function addCampaignOffer(
  client: ApiClient,
  campaignId: string,
  body: AddCampaignOfferRequest,
  options: CallOptions = {},
): Promise<S['AdminCampaignOffer']> {
  return client.post(`/v1/campaigns/${campaignId}/offers`, { body, ...options });
}

export function removeCampaignOffer(
  client: ApiClient,
  campaignId: string,
  offerId: string,
  options: CallOptions = {},
): Promise<void> {
  return client.delete(`/v1/campaigns/${campaignId}/offers/${offerId}`, { ...options });
}

// ── order ─────────────────────────────────────────────────────────────────

export function listOrders(
  client: ApiClient,
  query: ListAdminOrdersQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['AdminOrderListItem']>> {
  return client.get('/v1/orders', { query: query as QueryRecord, ...options });
}

export function getOrder(
  client: ApiClient,
  orderId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['AdminOrder']> {
  return client.get(`/v1/orders/${orderId}`, options);
}

export function cancelOrder(
  client: ApiClient,
  orderId: string,
  body: CancelAdminOrderRequest,
  options: CallOptions = {},
): Promise<S['AdminOrder']> {
  return client.post(`/v1/orders/${orderId}/cancel`, { body, ...options });
}

export function cancelOrderLine(
  client: ApiClient,
  orderId: string,
  lineId: string,
  body: CancelAdminOrderRequest,
  options: CallOptions = {},
): Promise<S['AdminOrder']> {
  return client.post(`/v1/orders/${orderId}/lines/${lineId}/cancel`, { body, ...options });
}

// ── ledger ────────────────────────────────────────────────────────────────

export function listLedgerEntries(
  client: ApiClient,
  query: ListLedgerEntriesQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['JournalEntry']>> {
  return client.get('/v1/ledger/entries', { query: query as QueryRecord, ...options });
}

export function getCampaignMargin(
  client: ApiClient,
  campaignId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['CampaignMargin']> {
  return client.get(`/v1/ledger/campaign-margin/${campaignId}`, options);
}

export function getLiabilityVsCash(
  client: ApiClient,
  options: { signal?: AbortSignal } = {},
): Promise<S['LiabilityVsCash']> {
  return client.get('/v1/ledger/liability-vs-cash', options);
}
