/**
 * Storefront BFF（:5000）的 MSW handlers。**覆蓋 `docs/05-API契約.md` §8 列出的
 * 全部 M1a 端點**（`/v1/webhooks/ecpay` 除外——那支不是給前端呼叫的）。
 *
 * 購物車與訂單用模組層級的可變陣列模擬「這次瀏覽階段的狀態」，
 * 讓「加入購物車 → 改數量 → 結帳 → 查訂單」這條路徑在 mock 下也能走得通。
 * 測試之間要乾淨的話呼叫 {@link resetStorefrontMockState}。
 */

import { http, HttpResponse } from 'msw';
import type { components } from '../types.storefront';
import {
  addresses as addressFixtures,
  buildCart,
  campaignDetailsById,
  campaignListItems,
  categories,
  findSkuById,
  meFixture,
  orderListItemOf,
  orders as orderFixtures,
  productDetailsById,
  productListItems,
  storedValueBalance,
  initialCartLines,
  CART_ID,
} from './fixtures.storefront';
import { hexId } from './ids';
import { paginate } from './pagination';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

export const STOREFRONT_BASE_URL = 'http://localhost:5000';

function url(path: string): string {
  return `${STOREFRONT_BASE_URL}${path}`;
}

// ── 可變狀態（模擬這次瀏覽階段） ────────────────────────────────────────

let me: S['Me'] = { ...meFixture };
let addressList: S['ShippingAddress'][] = addressFixtures.map((a) => ({ ...a }));
let cartLines: S['CartLine'][] = initialCartLines();
let cartQuote: S['QuoteResult'] | null = null;
let ordersStore: S['Order'][] = orderFixtures.map((o) => ({ ...o }));

/** 測試之間重置 mock 的可變狀態，避免前一個測試的購物車／訂單影響下一個。 */
export function resetStorefrontMockState(): void {
  me = { ...meFixture };
  addressList = addressFixtures.map((a) => ({ ...a }));
  cartLines = initialCartLines();
  cartQuote = null;
  ordersStore = orderFixtures.map((o) => ({ ...o }));
}

function currentCart(): S['Cart'] {
  return buildCart(CART_ID, cartLines, cartQuote);
}

const SHIPPING_FEE_BY_METHOD: Record<S['DeliveryMethod'], number> = {
  ConvenienceStore: 6000,
  HomeDelivery: 12000,
  SelfPickup: 0,
};

const SHIPPING_EXPLAIN_BY_METHOD: Record<S['DeliveryMethod'], string> = {
  ConvenienceStore: '超商取貨一口價 NT$60（ADR-010）。',
  HomeDelivery: '宅配到府一口價 NT$120（ADR-010）。',
  SelfPickup: '自取免運費。',
};

function buildQuote(deliveryMethod: S['DeliveryMethod']): S['QuoteResult'] {
  const goodsTotalMinor = cartLines.reduce((sum, l) => sum + l.lineTotal.amountMinor, 0);
  const shippingFeeMinor = SHIPPING_FEE_BY_METHOD[deliveryMethod];
  return {
    deliveryMethod,
    goodsTotal: { amountMinor: goodsTotalMinor, currency: 'TWD' },
    shippingFee: { amountMinor: shippingFeeMinor, currency: 'TWD' },
    grandTotal: { amountMinor: goodsTotalMinor + shippingFeeMinor, currency: 'TWD' },
    appliedStrategy: 'Flat',
    explain: [SHIPPING_EXPLAIN_BY_METHOD[deliveryMethod]],
  };
}

export const storefrontHandlers = [
  // ── auth ──────────────────────────────────────────────────────────────
  http.post(url('/v1/auth/register'), () => HttpResponse.json(me, { status: 201 })),

  http.post(url('/v1/auth/login'), () => HttpResponse.json(me, { status: 200 })),

  http.post(url('/v1/auth/logout'), () => new HttpResponse(null, { status: 204 })),

  // ── me ────────────────────────────────────────────────────────────────
  http.get(url('/v1/me'), () => HttpResponse.json(me)),

  http.patch(url('/v1/me'), async ({ request }) => {
    const body = (await request.json()) as { displayName?: string; email?: string | null };
    me = { ...me, ...body };
    return HttpResponse.json(me);
  }),

  http.get(url('/v1/me/addresses'), () => HttpResponse.json(addressList)),

  http.post(url('/v1/me/addresses'), async ({ request }) => {
    const body = (await request.json()) as S['ShippingAddressInput'];
    const created: S['ShippingAddress'] = { id: hexId(`address:${Date.now()}:${addressList.length}`), ...body };
    addressList = [...addressList, created];
    return HttpResponse.json(created, { status: 201 });
  }),

  http.put(url('/v1/me/addresses/:addressId'), async ({ request, params }) => {
    const { addressId } = params;
    const index = addressList.findIndex((a) => a.id === addressId);
    if (index === -1) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這筆地址。'));
    }
    const body = (await request.json()) as S['ShippingAddressInput'];
    const updated: S['ShippingAddress'] = { id: String(addressId), ...body };
    addressList = addressList.map((a, i) => (i === index ? updated : a));
    return HttpResponse.json(updated);
  }),

  http.delete(url('/v1/me/addresses/:addressId'), ({ params }) => {
    const { addressId } = params;
    if (!addressList.some((a) => a.id === addressId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這筆地址。'));
    }
    addressList = addressList.filter((a) => a.id !== addressId);
    return new HttpResponse(null, { status: 204 });
  }),

  http.get(url('/v1/me/stored-value'), () => HttpResponse.json(storedValueBalance)),

  // ── catalog ───────────────────────────────────────────────────────────
  http.get(url('/v1/categories'), () => HttpResponse.json(categories)),

  http.get(url('/v1/products'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const categoryId = q.get('categoryId') ?? undefined;
    const keyword = q.get('q')?.toLowerCase() ?? undefined;
    const mode = (q.get('mode') as S['FulfillmentMode'] | null) ?? undefined;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;

    const filtered = productListItems.filter((p) => {
      if (categoryId) {
        const detail = productDetailsById.get(p.id);
        if (detail?.categoryId !== categoryId) return false;
      }
      if (mode && p.mode !== mode) return false;
      if (keyword && !p.name.toLowerCase().includes(keyword)) return false;
      return true;
    });

    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.get(url('/v1/products/:productId'), ({ params }) => {
    const detail = productDetailsById.get(String(params.productId));
    if (!detail) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    return HttpResponse.json(detail);
  }),

  // ── campaign ──────────────────────────────────────────────────────────
  http.get(url('/v1/campaigns'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const status = (q.get('status') as S['CampaignStatus'] | null) ?? 'Open';
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = campaignListItems.filter((c) => c.status === status);
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.get(url('/v1/campaigns/:campaignId'), ({ params }) => {
    const detail = campaignDetailsById.get(String(params.campaignId));
    if (!detail) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    return HttpResponse.json(detail);
  }),

  // ── cart ──────────────────────────────────────────────────────────────
  http.get(url('/v1/cart'), () => HttpResponse.json(currentCart())),

  http.post(url('/v1/cart/lines'), async ({ request }) => {
    const body = (await request.json()) as {
      skuId: string;
      mode: S['FulfillmentMode'];
      campaignOfferId?: string | null;
      quantity: number;
    };
    const found = findSkuById(body.skuId);
    if (!found) {
      return jsonProblem(problem(422, 'catalog.sku-not-found', '找不到這個品項。'));
    }
    const newLine: S['CartLine'] = {
      id: hexId(`cartline:${body.skuId}:${Date.now()}`),
      skuId: found.sku.id,
      productId: found.product.id,
      name: found.product.name,
      variantName: found.sku.variantName ?? null,
      imageUrl: null,
      mode: body.mode,
      campaignId: found.product.campaign?.id ?? null,
      campaignOfferId: body.campaignOfferId ?? found.sku.campaignOfferId ?? null,
      quantity: body.quantity,
      unitPrice: found.sku.price ?? { amountMinor: 0, currency: 'TWD' },
      lineTotal: {
        amountMinor: (found.sku.price?.amountMinor ?? 0) * body.quantity,
        currency: 'TWD',
      },
      availabilityWarning: null,
    };
    cartLines = [...cartLines, newLine];
    return HttpResponse.json(currentCart());
  }),

  http.patch(url('/v1/cart/lines/:lineId'), async ({ request, params }) => {
    const { lineId } = params;
    const index = cartLines.findIndex((l) => l.id === lineId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個購物車品項。'));
    const body = (await request.json()) as { quantity: number };
    const target = cartLines[index];
    if (!target) return jsonProblem(problem(404, 'platform.not-found', '找不到這個購物車品項。'));
    const updated: S['CartLine'] = {
      ...target,
      quantity: body.quantity,
      lineTotal: { amountMinor: target.unitPrice.amountMinor * body.quantity, currency: 'TWD' },
    };
    cartLines = cartLines.map((l, i) => (i === index ? updated : l));
    return HttpResponse.json(currentCart());
  }),

  http.delete(url('/v1/cart/lines/:lineId'), ({ params }) => {
    const { lineId } = params;
    if (!cartLines.some((l) => l.id === lineId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這個購物車品項。'));
    }
    cartLines = cartLines.filter((l) => l.id !== lineId);
    return HttpResponse.json(currentCart());
  }),

  http.post(url('/v1/cart/quote'), async ({ request }) => {
    if (cartLines.length === 0) {
      return jsonProblem(problem(422, 'checkout.cart-empty', '購物車是空的'));
    }
    const body = (await request.json()) as { deliveryMethod: S['DeliveryMethod'] };
    cartQuote = buildQuote(body.deliveryMethod);
    return HttpResponse.json(cartQuote);
  }),

  http.post(url('/v1/cart/checkout'), async ({ request }) => {
    if (cartLines.length === 0) {
      return jsonProblem(problem(422, 'checkout.cart-empty', '購物車是空的'));
    }
    const body = (await request.json()) as {
      deliveryMethod: S['DeliveryMethod'];
      shippingPolicy: S['ShippingPolicy'];
      shippingAddressId?: string | null;
      convenienceStoreCode?: string | null;
      buyerNote?: string | null;
    };
    if (body.deliveryMethod === 'HomeDelivery' && !body.shippingAddressId) {
      return jsonProblem(problem(422, 'checkout.address-required', '請選擇收件地址'));
    }
    if (body.deliveryMethod === 'ConvenienceStore' && !body.convenienceStoreCode) {
      return jsonProblem(problem(422, 'checkout.store-code-required', '請選擇取貨門市'));
    }
    const quote = buildQuote(body.deliveryMethod);
    const address = body.shippingAddressId ? addressList.find((a) => a.id === body.shippingAddressId) ?? null : null;
    const orderIndex = ordersStore.length + 1;
    const newOrder: S['Order'] = {
      id: hexId(`order:checkout:${Date.now()}`),
      orderNumber: `GG${new Date().toISOString().slice(2, 10).replace(/-/g, '')}${String(orderIndex).padStart(3, '0')}`,
      status: 'AwaitingPayment',
      shippingPolicy: body.shippingPolicy,
      deliveryMethod: body.deliveryMethod,
      goodsTotal: quote.goodsTotal,
      shippingFee: quote.shippingFee,
      grandTotal: quote.grandTotal,
      paidAmount: null,
      lines: cartLines.map((line) => ({
        id: hexId(`orderline:${line.id}`),
        skuId: line.skuId,
        productId: line.productId,
        name: line.name,
        variantName: line.variantName ?? null,
        imageUrl: line.imageUrl ?? null,
        mode: line.mode,
        status: 'Pending',
        quantity: line.quantity,
        unitPrice: line.unitPrice,
        lineTotal: line.lineTotal,
        campaignId: line.campaignId ?? null,
        refundedAmount: null,
      })),
      shippingAddress: address,
      convenienceStoreName: body.convenienceStoreCode ? '7-ELEVEN 信義門市' : null,
      placedAt: new Date().toISOString(),
      paymentDueAt: new Date(Date.now() + 2 * 3_600_000).toISOString(),
      quoteExplain: quote.explain,
    };
    ordersStore = [newOrder, ...ordersStore];
    cartLines = [];
    cartQuote = null;
    return HttpResponse.json(newOrder, { status: 201 });
  }),

  // ── order ─────────────────────────────────────────────────────────────
  http.get(url('/v1/orders'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const status = q.get('status') as S['OrderStatus'] | null;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = ordersStore.filter((o) => !status || o.status === status).map(orderListItemOf);
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.get(url('/v1/orders/:orderId'), ({ params }) => {
    const order = ordersStore.find((o) => o.id === params.orderId);
    if (!order) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    return HttpResponse.json(order);
  }),

  http.post(url('/v1/orders/:orderId/cancel'), ({ params }) => {
    const index = ordersStore.findIndex((o) => o.id === params.orderId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    const order = ordersStore[index];
    if (!order) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    if (order.status !== 'AwaitingPayment') {
      return jsonProblem(
        problem(422, 'ordering.cannot-self-cancel-after-payment', '已付款的訂單無法自助取消，請聯繫客服。'),
      );
    }
    const cancelled: S['Order'] = { ...order, status: 'Cancelled' };
    ordersStore = ordersStore.map((o, i) => (i === index ? cancelled : o));
    return HttpResponse.json(cancelled);
  }),

  // ── payment ───────────────────────────────────────────────────────────
  http.post(url('/v1/orders/:orderId/payment'), ({ params }) => {
    const order = ordersStore.find((o) => o.id === params.orderId);
    if (!order) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    if (order.status === 'Cancelled') {
      return jsonProblem(problem(409, 'ordering.order-cancelled', '這筆訂單已經取消。'));
    }
    if (order.status !== 'AwaitingPayment') {
      return jsonProblem(problem(409, 'ordering.order-already-paid', '這筆訂單已經付款了。'));
    }
    const initiation: S['PaymentInitiation'] = {
      provider: 'ECPay',
      method: 'POST',
      action: 'https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5',
      fields: {
        MerchantID: '3002607',
        MerchantTradeNo: order.orderNumber,
        TotalAmount: String(order.grandTotal.amountMinor / 100),
        TradeDesc: 'GreyGray 代購訂單',
        CheckMacValue: hexId(`checkmac:${order.id}`).toUpperCase(),
      },
      expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
    };
    return HttpResponse.json(initiation);
  }),

  // ── fulfillment（M1b，M1a 期間恆回空陣列）────────────────────────────
  http.get(url('/v1/orders/:orderId/shipments'), ({ params }) => {
    if (!ordersStore.some((o) => o.id === params.orderId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    }
    return HttpResponse.json([]);
  }),
];

/** 個別端點的錯誤情境，供頁面測試「這一次剛好出錯」用（`server.use()` / `worker.use()` 疊加）。 */
export const storefrontErrorScenarios = {
  registerPhoneAlreadyRegistered: http.post(url('/v1/auth/register'), () =>
    jsonProblem(
      problem(422, 'identity.phone-already-registered', '這個手機號碼已經註冊過了', {
        errors: { phoneNumber: ['這個手機號碼已經註冊過了。'] },
      }),
    ),
  ),
  registerWeakPassword: http.post(url('/v1/auth/register'), () =>
    jsonProblem(
      problem(422, 'identity.weak-password', '密碼強度不足', {
        errors: { password: ['密碼至少需要 8 碼，並包含英文與數字。'] },
      }),
    ),
  ),
  loginInvalidCredentials: http.post(url('/v1/auth/login'), () =>
    jsonProblem(problem(401, 'identity.invalid-credentials', '手機號碼或密碼錯誤')),
  ),
  cartLineInsufficientStock: http.post(url('/v1/cart/lines'), () =>
    jsonProblem(problem(422, 'inventory.insufficient-stock', '庫存只剩 2 件，無法加入這個數量')),
  ),
  checkoutConflict: http.post(url('/v1/cart/checkout'), () =>
    jsonProblem(problem(409, 'platform.request-in-flight', '上一筆請求還在處理中，請稍後再試')),
  ),
  paymentAlreadyPaid: http.post(url('/v1/orders/:orderId/payment'), () =>
    jsonProblem(problem(409, 'ordering.order-already-paid', '這筆訂單已經付款了。')),
  ),
  serverError: http.get(url('/v1/products'), () => jsonProblem(problem(500, 'platform.unexpected', '系統發生問題，請稍後再試。'))),
};
