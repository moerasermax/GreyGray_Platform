/**
 * Storefront BFF（:5000）端點層。**只負責把路徑、query、body 組對，型別接對**，
 * 不做任何轉換——轉換（金額格式化、enum 顯示）是呼叫端的事。
 *
 * 契約沒有 `operationId`，函式名是這一包自己取的。方法 ＋ 路徑 → 函式名對照表
 * 見 `frontend/packages/api-client/src/endpoints/README.md`。
 */

import type { ApiClient, Page } from '../http';
import type { components, paths } from '../types.storefront';

type S = components['schemas'];
/** `RequestOptions.query` 要求索引簽章；具名 query 介面沒有，這裡轉一手。 */
type QueryRecord = Record<string, string | number | boolean | undefined | null>;

/** 寫入請求共用的選項：契約要求冪等鍵，端點層不允許省略。 */
export interface MutationOptions {
  readonly idempotencyKey: string;
  readonly signal?: AbortSignal;
}

export interface LoginRequest {
  readonly phoneNumber: string;
  readonly password: string;
}

export interface UpdateMeRequest {
  readonly displayName?: string;
  readonly email?: string | null;
}

export interface AddCartLineRequest {
  readonly skuId: string;
  readonly mode: S['FulfillmentMode'];
  readonly campaignOfferId?: string | null;
  readonly quantity: number;
}

export interface UpdateCartLineRequest {
  readonly quantity: number;
}

export interface QuoteCartRequest {
  readonly deliveryMethod: S['DeliveryMethod'];
}

export interface CheckoutRequest {
  readonly deliveryMethod: S['DeliveryMethod'];
  /** ADR-030：只有 `Cart.hasMixedModes` 時必填；單一模式可省略或 `null`，後端依 line 組成推導。 */
  readonly shippingPolicy?: S['ShippingPolicy'] | null;
  readonly shippingAddressId?: string | null;
  /** ADR-038：超商取貨帶選店票，後端從票取出門市代號、名稱、地址凍結進訂單。同時帶了代號時以這個為準。 */
  readonly convenienceStoreSelectionId?: string | null;
  /** 相容舊用戶端（契約保留）：沒帶 `convenienceStoreSelectionId` 時才看這個。 */
  readonly convenienceStoreCode?: string | null;
  /**
   * 收件人真實姓名（ADR-039）。`deliveryMethod = ConvenienceStore` 時必填，缺了回
   * `422 checkout.recipient-required`；`HomeDelivery` 時送了會被忽略（後端從地址簿抄）。
   */
  readonly recipientName?: string | null;
  /** 收件人手機（ADR-039）。必填與忽略的規則同 `recipientName`。 */
  readonly recipientPhone?: string | null;
  readonly buyerNote?: string | null;
}

export interface CancelOrderRequest {
  readonly reason?: string | null;
}

export interface ListProductsQuery {
  readonly categoryId?: string;
  readonly q?: string;
  readonly mode?: S['FulfillmentMode'];
  readonly includeDescendants?: boolean;
  readonly cursor?: string;
  readonly limit?: number;
}

export interface ListCampaignsQuery {
  readonly status?: S['CampaignStatus'];
  readonly cursor?: string;
  readonly limit?: number;
}

export interface ListOrdersQuery {
  readonly status?: S['OrderStatus'];
  readonly cursor?: string;
  readonly limit?: number;
}

type ListFavoritesOperation = paths['/v1/me/favorites']['get'];
export type ListFavoritesQuery = NonNullable<ListFavoritesOperation['parameters']['query']>;
type ListFavoritesResponse = ListFavoritesOperation['responses'][200]['content']['application/json'];
type FavoriteProductId = paths['/v1/me/favorites/{productId}']['parameters']['path']['productId'];

// ── auth ──────────────────────────────────────────────────────────────────

export function register(
  client: ApiClient,
  body: S['RegisterRequest'],
  options: MutationOptions,
): Promise<S['Me']> {
  return client.post('/v1/auth/register', { body, ...options });
}

export function login(client: ApiClient, body: LoginRequest, options: MutationOptions): Promise<S['Me']> {
  return client.post('/v1/auth/login', { body, ...options });
}

export function logout(client: ApiClient, options: MutationOptions): Promise<void> {
  return client.post('/v1/auth/logout', { ...options });
}

// ── me ────────────────────────────────────────────────────────────────────

export function getMe(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['Me']> {
  return client.get('/v1/me', options);
}

export function updateMe(
  client: ApiClient,
  body: UpdateMeRequest,
  options: MutationOptions,
): Promise<S['Me']> {
  return client.patch('/v1/me', { body, ...options });
}

export function listAddresses(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['ShippingAddress'][]> {
  return client.get('/v1/me/addresses', options);
}

export function createAddress(
  client: ApiClient,
  body: S['ShippingAddressInput'],
  options: MutationOptions,
): Promise<S['ShippingAddress']> {
  return client.post('/v1/me/addresses', { body, ...options });
}

export function updateAddress(
  client: ApiClient,
  addressId: string,
  body: S['ShippingAddressInput'],
  options: MutationOptions,
): Promise<S['ShippingAddress']> {
  return client.put(`/v1/me/addresses/${addressId}`, { body, ...options });
}

export function deleteAddress(client: ApiClient, addressId: string, options: MutationOptions): Promise<void> {
  return client.delete(`/v1/me/addresses/${addressId}`, { ...options });
}

export function getStoredValueBalance(
  client: ApiClient,
  options: { signal?: AbortSignal } = {},
): Promise<{ balance: S['Money'] }> {
  return client.get('/v1/me/stored-value', options);
}

export function listFavorites(
  client: ApiClient,
  query: ListFavoritesQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<ListFavoritesResponse> {
  return client.get('/v1/me/favorites', { query: query as QueryRecord, ...options });
}

export function addFavorite(
  client: ApiClient,
  productId: FavoriteProductId,
  options: MutationOptions,
): Promise<void> {
  return client.put(`/v1/me/favorites/${productId}`, { ...options });
}

export function removeFavorite(
  client: ApiClient,
  productId: FavoriteProductId,
  options: MutationOptions,
): Promise<void> {
  return client.delete(`/v1/me/favorites/${productId}`, { ...options });
}

// ── catalog ───────────────────────────────────────────────────────────────

export function listCategories(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['Category'][]> {
  return client.get('/v1/categories', options);
}

export function listProducts(
  client: ApiClient,
  query: ListProductsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['ProductListItem']>> {
  return client.get('/v1/products', { query: query as QueryRecord, ...options });
}

export function getProduct(
  client: ApiClient,
  productId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['ProductDetail']> {
  return client.get(`/v1/products/${productId}`, options);
}

// ── campaign ──────────────────────────────────────────────────────────────

export function listCampaigns(
  client: ApiClient,
  query: ListCampaignsQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['CampaignListItem']>> {
  return client.get('/v1/campaigns', { query: query as QueryRecord, ...options });
}

export function getCampaign(
  client: ApiClient,
  campaignId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['CampaignDetail']> {
  return client.get(`/v1/campaigns/${campaignId}`, options);
}

// ── cart ──────────────────────────────────────────────────────────────────

export function getCart(client: ApiClient, options: { signal?: AbortSignal } = {}): Promise<S['Cart']> {
  return client.get('/v1/cart', options);
}

export function addCartLine(
  client: ApiClient,
  body: AddCartLineRequest,
  options: MutationOptions,
): Promise<S['Cart']> {
  return client.post('/v1/cart/lines', { body, ...options });
}

export function updateCartLine(
  client: ApiClient,
  lineId: string,
  body: UpdateCartLineRequest,
  options: MutationOptions,
): Promise<S['Cart']> {
  return client.patch(`/v1/cart/lines/${lineId}`, { body, ...options });
}

export function removeCartLine(client: ApiClient, lineId: string, options: MutationOptions): Promise<S['Cart']> {
  return client.delete(`/v1/cart/lines/${lineId}`, { ...options });
}

export function quoteCart(
  client: ApiClient,
  body: QuoteCartRequest,
  options: MutationOptions,
): Promise<S['QuoteResult']> {
  return client.post('/v1/cart/quote', { body, ...options });
}

export function checkout(
  client: ApiClient,
  body: CheckoutRequest,
  options: MutationOptions,
): Promise<S['Order']> {
  return client.post('/v1/cart/checkout', { body, ...options });
}

// ── order ─────────────────────────────────────────────────────────────────

export function listOrders(
  client: ApiClient,
  query: ListOrdersQuery = {},
  options: { signal?: AbortSignal } = {},
): Promise<Page<S['OrderListItem']>> {
  return client.get('/v1/orders', { query: query as QueryRecord, ...options });
}

export function getOrder(
  client: ApiClient,
  orderId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['Order']> {
  return client.get(`/v1/orders/${orderId}`, options);
}

export function cancelOrder(
  client: ApiClient,
  orderId: string,
  body: CancelOrderRequest,
  options: MutationOptions,
): Promise<S['Order']> {
  return client.post(`/v1/orders/${orderId}/cancel`, { body, ...options });
}

// ── logistics（超商取貨門市，ADR-038）──────────────────────────────────────

type CreateCvsMapSessionOperation = paths['/v1/logistics/cvs-map-sessions']['post'];
/** 契約裡是 inline object、沒有 `S['…']`，從 `paths` 推導，不另外手寫一份。 */
export type CreateCvsMapSessionRequest =
  CreateCvsMapSessionOperation['requestBody']['content']['application/json'];
type CvsSelectionId = paths['/v1/logistics/cvs-selections/{selectionId}']['parameters']['path']['selectionId'];

/**
 * 開啟 7-ELEVEN 電子地圖（產生一次性選店票）。**沒有冪等鍵**——契約沒有要求，
 * 每開一次就是一張新票；連點由呼叫端的同步鎖擋。
 *
 * `POST /v1/logistics/cvs-map/reply` 不做函式：那是綠界經客人的瀏覽器 POST 回後端的，不是給前端呼叫的。
 */
export function createCvsMapSession(
  client: ApiClient,
  body: CreateCvsMapSessionRequest,
  options: { signal?: AbortSignal } = {},
): Promise<S['CvsMapSession']> {
  return client.post('/v1/logistics/cvs-map-sessions', { body, ...options });
}

/** 讀選好的門市。只有同一台購物車讀得到；不存在、過期、屬於別台購物車一律 `404`。 */
export function getCvsSelection(
  client: ApiClient,
  selectionId: CvsSelectionId,
  options: { signal?: AbortSignal } = {},
): Promise<S['CvsStoreSelection']> {
  return client.get(`/v1/logistics/cvs-selections/${encodeURIComponent(selectionId)}`, options);
}

// ── payment ───────────────────────────────────────────────────────────────

export function initiatePayment(
  client: ApiClient,
  orderId: string,
  options: MutationOptions,
): Promise<S['PaymentInitiation']> {
  return client.post(`/v1/orders/${orderId}/payment`, { ...options });
}

// ── fulfillment（M1b，M1a 期間恆回空陣列——見 docs/05-API契約.md §8）────────

export function listOrderShipments(
  client: ApiClient,
  orderId: string,
  options: { signal?: AbortSignal } = {},
): Promise<S['Shipment'][]> {
  return client.get(`/v1/orders/${orderId}/shipments`, options);
}
