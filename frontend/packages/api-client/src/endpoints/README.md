# 端點對照表

契約（`docs/api/openapi.*.yaml`）沒有 `operationId`，函式名是這一包自己取的。
第二波接的時候照這張表找函式，不要用路徑字串猜。

只列 M1a。M1b／M2／M3 的端點契約已定但實作延後，`endpoints/` 目前沒有包（`storefront.ts`
的 `listOrderShipments` 例外——M1a 期間它恆回空陣列，見檔案內註解）。

## `endpoints/storefront.ts`

| 方法 | 路徑 | 函式 |
|---|---|---|
| POST | `/v1/auth/register` | `register` |
| POST | `/v1/auth/login` | `login` |
| POST | `/v1/auth/logout` | `logout` |
| GET | `/v1/me` | `getMe` |
| PATCH | `/v1/me` | `updateMe` |
| GET | `/v1/me/addresses` | `listAddresses` |
| POST | `/v1/me/addresses` | `createAddress` |
| PUT | `/v1/me/addresses/{addressId}` | `updateAddress` |
| DELETE | `/v1/me/addresses/{addressId}` | `deleteAddress` |
| GET | `/v1/me/stored-value` | `getStoredValueBalance` |
| GET | `/v1/categories` | `listCategories` |
| GET | `/v1/products` | `listProducts` |
| GET | `/v1/products/{productId}` | `getProduct` |
| GET | `/v1/campaigns` | `listCampaigns` |
| GET | `/v1/campaigns/{campaignId}` | `getCampaign` |
| GET | `/v1/cart` | `getCart` |
| POST | `/v1/cart/lines` | `addCartLine` |
| PATCH | `/v1/cart/lines/{lineId}` | `updateCartLine` |
| DELETE | `/v1/cart/lines/{lineId}` | `removeCartLine` |
| POST | `/v1/cart/quote` | `quoteCart` |
| POST | `/v1/cart/checkout` | `checkout` |
| GET | `/v1/orders` | `listOrders` |
| GET | `/v1/orders/{orderId}` | `getOrder` |
| POST | `/v1/orders/{orderId}/cancel` | `cancelOrder` |
| POST | `/v1/orders/{orderId}/payment` | `initiatePayment` |
| GET | `/v1/orders/{orderId}/shipments` | `listOrderShipments`（M1b，M1a 期間回 `[]`） |

## `endpoints/admin.ts`

| 方法 | 路徑 | 函式 |
|---|---|---|
| POST | `/v1/auth/login` | `login` |
| POST | `/v1/auth/logout` | `logout` |
| GET | `/v1/me` | `getMe` |
| GET | `/v1/categories` | `listCategories` |
| POST | `/v1/categories` | `createCategory` |
| PATCH | `/v1/categories/{categoryId}` | `updateCategory` |
| GET | `/v1/products` | `listProducts` |
| POST | `/v1/products` | `createProduct` |
| GET | `/v1/products/{productId}` | `getProduct` |
| PATCH | `/v1/products/{productId}` | `updateProduct` |
| PATCH | `/v1/skus/{skuId}` | `updateSku` |
| GET | `/v1/campaigns` | `listCampaigns` |
| POST | `/v1/campaigns` | `createCampaign` |
| GET | `/v1/campaigns/{campaignId}` | `getCampaign` |
| PATCH | `/v1/campaigns/{campaignId}` | `updateCampaign` |
| POST | `/v1/campaigns/{campaignId}/publish` | `publishCampaign` |
| POST | `/v1/campaigns/{campaignId}/close` | `closeCampaign` |
| POST | `/v1/campaigns/{campaignId}/cancel` | `cancelCampaign` |
| POST | `/v1/campaigns/{campaignId}/settle` | `settleCampaign` |
| GET | `/v1/campaigns/{campaignId}/offers` | `listCampaignOffers` |
| POST | `/v1/campaigns/{campaignId}/offers` | `addCampaignOffer` |
| DELETE | `/v1/campaigns/{campaignId}/offers/{offerId}` | `removeCampaignOffer` |
| GET | `/v1/orders` | `listOrders` |
| GET | `/v1/orders/{orderId}` | `getOrder` |
| POST | `/v1/orders/{orderId}/cancel` | `cancelOrder` |
| POST | `/v1/orders/{orderId}/lines/{lineId}/cancel` | `cancelOrderLine` |
| GET | `/v1/ledger/entries` | `listLedgerEntries` |
| GET | `/v1/ledger/campaign-margin/{campaignId}` | `getCampaignMargin` |
| GET | `/v1/ledger/liability-vs-cash` | `getLiabilityVsCash` |

## 建立 client 的方式

```ts
import { ApiClient } from '@greygray/api-client';
import * as storefrontApi from '@greygray/api-client/endpoints/storefront'; // 見交付回報：exports 待整合者加

const client = new ApiClient({ baseUrl: 'http://localhost:5000' });
const me = await storefrontApi.getMe(client);
```

`endpoints/*.ts` 的函式簽章一律是 `(client, ...參數, options?) => Promise<T>`，
`options` 只有寫入端點才有，形狀是 `{ idempotencyKey?, signal? }`。
