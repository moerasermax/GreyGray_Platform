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
function cloneOrders(): S['Order'][] {
  return orderFixtures.map((order) => ({
    ...order,
    goodsTotal: { ...order.goodsTotal },
    shippingFee: { ...order.shippingFee },
    grandTotal: { ...order.grandTotal },
    paidAmount: order.paidAmount ? { ...order.paidAmount } : null,
    lines: order.lines.map((line) => ({
      ...line,
      unitPrice: { ...line.unitPrice },
      lineTotal: { ...line.lineTotal },
      refundedAmount: line.refundedAmount ? { ...line.refundedAmount } : null,
    })),
    shippingAddress: order.shippingAddress ? { ...order.shippingAddress } : null,
    paymentInstructions: order.paymentInstructions
      ? { ...order.paymentInstructions, barcodes: order.paymentInstructions.barcodes ? [...order.paymentInstructions.barcodes] : null }
      : null,
    quoteExplain: order.quoteExplain ? [...order.quoteExplain] : [],
  }));
}

let ordersStore: S['Order'][] = cloneOrders();
const initialFavoriteProductIds = () =>
  productListItems.filter((product) => product.isFavorited).map((product) => product.id).reverse();
let favoriteProductIds: string[] = initialFavoriteProductIds();

/** 開出去的選店票（ADR-038）。mock 只記票，讀到的門市一律是 {@link MOCK_CVS_STORE}。 */
let cvsSelectionIds: string[] = [];
let cvsSelectionSequence = 0;

export const MOCK_CVS_STORE = {
  storeCode: '991234',
  storeName: '7-ELEVEN 信義門市',
  storeAddress: '台北市信義區松仁路 100 號',
  isOutlying: false,
} as const;

/** 20 個英數，跟後端選店票同形。時間前綴讓整頁重新載入之後（序號歸零）也不會撞號。 */
function nextMockCvsSelectionId(): string {
  cvsSelectionSequence += 1;
  return `MOCKCVS${String(Date.now() % 10_000_000).padStart(7, '0')}${String(cvsSelectionSequence).padStart(6, '0')}`;
}

/** 測試之間重置 mock 的可變狀態，避免前一個測試的購物車／訂單影響下一個。 */
export function resetStorefrontMockState(): void {
  me = { ...meFixture };
  addressList = addressFixtures.map((a) => ({ ...a }));
  cartLines = initialCartLines();
  cartQuote = null;
  ordersStore = cloneOrders();
  favoriteProductIds = initialFavoriteProductIds();
  cvsSelectionIds = [];
  cvsSelectionSequence = 0;
  if (typeof sessionStorage !== 'undefined') sessionStorage.removeItem(SESSION_KEY);
}

/*
 * ── 為什麼要存進 sessionStorage ──
 *
 * msw 的 browser worker 是在**頁面自己的 JS context** 裡跑 handler，
 * service worker 只負責把請求轉進來。所以整頁導覽會把上面那些 `let` 全部清空。
 *
 * 付款正好就是整頁導覽（`PaymentInitiation` 要求用 form POST 到 `action`），
 * 於是「結帳建立訂單 → 導向收銀台 → 導回結果頁 → GET /v1/orders/{id}」
 * 最後一步必定 404——第二波整合測試就是這樣撞到的，畫面顯示「找不到這張訂單」。
 *
 * 存進 sessionStorage 之後，語意剛好就是上面註解寫的「這次瀏覽階段」。
 * Node 端（vitest／SSR）沒有 sessionStorage，就自動退回純記憶體，行為不變。
 */
const SESSION_KEY = 'gg-mock-storefront-state';

function loadSession(): void {
  if (typeof sessionStorage === 'undefined') return;
  try {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return;
    const d = JSON.parse(raw) as Partial<{
      me: S['Me']; addressList: S['ShippingAddress'][]; cartLines: S['CartLine'][];
      cartQuote: S['QuoteResult'] | null; ordersStore: S['Order'][]; favoriteProductIds: string[];
      cvsSelectionIds: string[];
    }>;
    if (d.me) me = d.me;
    if (d.addressList) addressList = d.addressList;
    if (d.cartLines) cartLines = d.cartLines;
    if (d.cartQuote !== undefined) cartQuote = d.cartQuote;
    if (d.ordersStore) ordersStore = d.ordersStore;
    if (d.favoriteProductIds) favoriteProductIds = d.favoriteProductIds;
    // 選門市也是整頁導覽（地圖表單 POST），不存的話回來讀票必定 404。
    if (d.cvsSelectionIds) cvsSelectionIds = d.cvsSelectionIds;
  } catch {
    // 壞掉的話就用初始 fixture，不要讓 mock 自己炸掉整頁
  }
}

function saveSession(): void {
  if (typeof sessionStorage === 'undefined') return;
  try {
    sessionStorage.setItem(SESSION_KEY,
      JSON.stringify({ me, addressList, cartLines, cartQuote, ordersStore, favoriteProductIds, cvsSelectionIds }));
  } catch {
    // 配額滿了就算了，mock 不值得為此中斷
  }
}

if (typeof window !== 'undefined') {
  loadSession();
  // pagehide 在表單 POST 導覽時也會觸發，比 beforeunload 可靠
  window.addEventListener('pagehide', saveSession);
}

function currentCart(): S['Cart'] {
  return buildCart(CART_ID, cartLines, cartQuote);
}

function orderWithDynamicPaymentOverdue(order: S['Order'], now = Date.now()): S['Order'] {
  if (order.paymentOverdue !== undefined) return order;
  const dueAt = order.paymentDueAt ? Date.parse(order.paymentDueAt) : Number.NaN;
  const instructionsExpireAt = order.paymentInstructions?.expiresAt
    ? Date.parse(order.paymentInstructions.expiresAt)
    : Number.NaN;
  const hasValidInstructions = Number.isFinite(instructionsExpireAt) && instructionsExpireAt > now;
  const paymentOverdue = order.status === 'AwaitingPayment'
    && Number.isFinite(dueAt)
    && dueAt <= now
    && !hasValidInstructions;
  return paymentOverdue ? { ...order, paymentOverdue: true, paymentInstructions: null } : order;
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

  http.get(url('/v1/me/favorites'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const cursor = q.get('cursor') ?? undefined;
    const limitText = q.get('limit');
    const limit = limitText === null ? undefined : Number(limitText);
    const invalidCursor = cursor !== undefined && !/^\d+$/.test(cursor);
    const invalidLimit = limit !== undefined && (!Number.isInteger(limit) || limit < 1 || limit > 100);
    if (invalidCursor || invalidLimit) {
      return jsonProblem(problem(422, 'catalog.invalid-cursor', '分頁游標或筆數不正確。'));
    }

    const items = favoriteProductIds
      .map((productId) => productListItems.find((product) => product.id === productId))
      .filter((product): product is S['ProductListItem'] => product !== undefined)
      .map((product) => ({ ...product, isFavorited: true }));
    return HttpResponse.json(paginate(items, cursor, limit));
  }),

  http.put(url('/v1/me/favorites/:productId'), ({ params }) => {
    const productId = String(params.productId);
    if (!productDetailsById.has(productId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    }
    if (!favoriteProductIds.includes(productId)) {
      favoriteProductIds = [productId, ...favoriteProductIds];
    }
    return new HttpResponse(null, { status: 204 });
  }),

  http.delete(url('/v1/me/favorites/:productId'), ({ params }) => {
    const productId = String(params.productId);
    if (!/^[0-9a-f]{32}$/i.test(productId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    }
    favoriteProductIds = favoriteProductIds.filter((id) => id !== productId);
    return new HttpResponse(null, { status: 204 });
  }),

  // ── catalog ───────────────────────────────────────────────────────────
  http.get(url('/v1/categories'), () => {
    const directlyPublishedIds = new Set(
      productListItems.flatMap((product) => {
        const categoryId = productDetailsById.get(product.id)?.categoryId;
        return categoryId ? [categoryId] : [];
      }),
    );
    const publishedIds = new Set(directlyPublishedIds);
    for (const category of categories) {
      if (category.parentId && directlyPublishedIds.has(category.id)) publishedIds.add(category.parentId);
    }
    return HttpResponse.json(categories.filter((category) => publishedIds.has(category.id)));
  }),

  http.get(url('/v1/products'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const categoryId = q.get('categoryId') ?? undefined;
    const includeDescendants = q.get('includeDescendants') === 'true';
    const keyword = q.get('q')?.toLowerCase() ?? undefined;
    const mode = (q.get('mode') as S['FulfillmentMode'] | null) ?? undefined;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;

    const filtered = productListItems.filter((p) => {
      if (categoryId) {
        const detail = productDetailsById.get(p.id);
        const acceptedCategoryIds = includeDescendants
          ? new Set([categoryId, ...categories.filter((category) => category.parentId === categoryId).map((category) => category.id)])
          : new Set([categoryId]);
        if (!detail?.categoryId || !acceptedCategoryIds.has(detail.categoryId)) return false;
      }
      if (mode && p.mode !== mode) return false;
      if (keyword && !p.name.toLowerCase().includes(keyword)) return false;
      return true;
    });

    return HttpResponse.json(paginate(
      filtered.map((product) => ({ ...product, isFavorited: favoriteProductIds.includes(product.id) })),
      cursor,
      limit,
    ));
  }),

  http.get(url('/v1/products/:productId'), ({ params }) => {
    const detail = productDetailsById.get(String(params.productId));
    if (!detail) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    return HttpResponse.json({ ...detail, isFavorited: favoriteProductIds.includes(detail.id) });
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
      convenienceStoreSelectionId?: string | null;
      convenienceStoreCode?: string | null;
      recipientName?: string | null;
      recipientPhone?: string | null;
      buyerNote?: string | null;
    };
    if (body.deliveryMethod === 'HomeDelivery' && !body.shippingAddressId) {
      return jsonProblem(problem(422, 'checkout.address-required', '請選擇收件地址'));
    }
    // ADR-039：超商取貨必填收件人姓名與手機；宅配的送了也不看，一律從地址簿抄。
    if (body.deliveryMethod === 'ConvenienceStore' && (!body.recipientName || !body.recipientPhone)) {
      return jsonProblem(problem(422, 'checkout.recipient-required', '請填寫收件人姓名與手機'));
    }
    // ADR-038：帶了選店票就以票為準、忽略代號；沒帶票才看舊的 `convenienceStoreCode`。
    const selectionId =
      body.deliveryMethod === 'ConvenienceStore' ? (body.convenienceStoreSelectionId ?? null) : null;
    if (selectionId !== null && !cvsSelectionIds.includes(selectionId)) {
      return jsonProblem(problem(422, 'checkout.store-selection-expired', '門市選擇已逾時，請重新選擇門市'));
    }
    if (body.deliveryMethod === 'ConvenienceStore' && selectionId === null && !body.convenienceStoreCode) {
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
      // ADR-039：超商取貨凍結送出的收件人；宅配從地址簿抄一份，客人事後改地址不影響已成立的訂單。
      recipientName: body.deliveryMethod === 'ConvenienceStore' ? (body.recipientName ?? null) : (address?.recipientName ?? null),
      recipientPhone: body.deliveryMethod === 'ConvenienceStore' ? (body.recipientPhone ?? null) : (address?.phoneNumber ?? null),
      convenienceStoreName: selectionId
        ? MOCK_CVS_STORE.storeName
        : body.deliveryMethod === 'ConvenienceStore' && body.convenienceStoreCode
          ? '7-ELEVEN 信義門市'
          : null,
      convenienceStoreAddress: selectionId ? MOCK_CVS_STORE.storeAddress : null,
      placedAt: new Date().toISOString(),
      paymentDueAt: new Date(Date.now() + 2 * 3_600_000).toISOString(),
      quoteExplain: quote.explain,
    };
    ordersStore = [newOrder, ...ordersStore];
    cartLines = [];
    cartQuote = null;
    return HttpResponse.json(newOrder, { status: 201 });
  }),

  // ── logistics（超商取貨門市，ADR-038）─────────────────────────────────
  http.post(url('/v1/logistics/cvs-map-sessions'), () => {
    const selectionId = nextMockCvsSelectionId();
    cvsSelectionIds = [...cvsSelectionIds, selectionId];
    const session: S['CvsMapSession'] = {
      selectionId,
      method: 'POST',
      /*
       * **mock 模式不可以指向真的綠界**（理由同付款的 `action`）。
       * mock 沒有假地圖可以選，直接送回結帳頁並帶上票，模擬「選好門市、後端 303 導回來」的結果。
       * `fields` 維持綠界地圖的形狀（`ExtraData` 就是選店票）。
       */
      action: `/checkout?cvsSelection=${selectionId}`,
      fields: {
        MerchantID: '2000933',
        LogisticsType: 'CVS',
        LogisticsSubType: 'UNIMARTC2C',
        IsCollection: 'N',
        ServerReplyURL: `${STOREFRONT_BASE_URL}/v1/logistics/cvs-map/reply`,
        ExtraData: selectionId,
      },
      expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    };
    return HttpResponse.json(session);
  }),

  http.get(url('/v1/logistics/cvs-selections/:selectionId'), ({ params }) => {
    const selectionId = String(params.selectionId);
    if (!cvsSelectionIds.includes(selectionId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這次的門市選擇。'));
    }
    const selection: S['CvsStoreSelection'] = {
      selectionId,
      ...MOCK_CVS_STORE,
      expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    };
    return HttpResponse.json(selection);
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
    return HttpResponse.json(orderWithDynamicPaymentOverdue(order));
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
    const cancelled: S['Order'] = {
      ...order,
      status: 'Cancelled',
      cancellationSource: 'Customer',
      cancelledAt: new Date().toISOString(),
      paymentOverdue: false,
      paymentInstructions: null,
    };
    ordersStore = ordersStore.map((o, i) => (i === index ? cancelled : o));
    return HttpResponse.json(cancelled);
  }),

  // ── payment ───────────────────────────────────────────────────────────
  http.post(url('/v1/orders/:orderId/payment'), ({ params }) => {
    const storedOrder = ordersStore.find((o) => o.id === params.orderId);
    if (!storedOrder) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    if (storedOrder.status === 'Cancelled') {
      return jsonProblem(problem(409, 'ordering.order-cancelled', '這筆訂單已經取消。'));
    }
    if (storedOrder.status !== 'AwaitingPayment') {
      return jsonProblem(problem(409, 'ordering.order-already-paid', '這筆訂單已經付款了。'));
    }
    const order = orderWithDynamicPaymentOverdue(storedOrder);
    if (order.paymentOverdue === true) {
      return jsonProblem(problem(422, 'ordering.payment-overdue', '繳費期限已過，正在確認付款。'));
    }
    const initiation: S['PaymentInitiation'] = {
      provider: 'ECPay',
      method: 'POST',
      /*
       * **mock 模式不可以指向真的綠界。**
       * 前端會照契約把 `fields` 原封不動 POST 到這個 `action`（docs/06 FE-4），
       * 所以這裡填綠界的網址＝每跑一次結帳測試就真的送一包資料到第三方，
       * 回來的還是「CheckMacValue Error」錯誤頁，看起來很像前端壞掉。
       *
       * 改指向 app 自己的假收銀台（`app/mock-cashier/route.ts`）。
       * 不能指向 `STOREFRONT_BASE_URL`（:5000）——表單 POST 是**導覽**，
       * service worker 只攔自己 scope 內的，跨來源到一個沒人在聽的埠會直接連線失敗。
       * 相對路徑會落在 app 自己的來源上，由真的 route handler 接。
       *
       * `orderId` 走 query 是這個假收銀台自己的接線，`fields` 維持綠界的形狀不動。
       */
      action: `/mock-cashier?orderId=${order.id}`,
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
  cvsMapNotConfigured: http.post(url('/v1/logistics/cvs-map-sessions'), () =>
    jsonProblem(problem(503, 'logistics.not-configured', '目前暫停超商取貨')),
  ),
};
