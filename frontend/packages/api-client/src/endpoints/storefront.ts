/**
 * Storefront BFF（:5000）端點層。**只負責把路徑、query、body 組對，型別接對**，
 * 不做任何轉換——轉換（金額格式化、enum 顯示）是呼叫端的事。
 *
 * 契約沒有 `operationId`，函式名是這一包自己取的。方法 ＋ 路徑 → 函式名對照表
 * 見 `frontend/packages/api-client/src/endpoints/README.md`。
 */

import type { ApiClient, Page } from '../http';
import type { components } from '../types.storefront';

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
  readonly convenienceStoreCode?: string | null;
  readonly buyerNote?: string | null;
}

export interface CancelOrderRequest {
  readonly reason?: string | null;
}

export interface ListProductsQuery {
  readonly categoryId?: string;
  readonly q?: string;
  readonly mode?: S['FulfillmentMode'];
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
