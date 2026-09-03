/**
 * Admin BFF（:5001）的 MSW handlers。**覆蓋 `docs/05-API契約.md` §8 列出的
 * 全部 M1a 端點**（procurement／fulfillment／trip-costs 是 M1b／M2，
 * 這一波不包，見 `endpoints/admin.ts` 檔頭）。
 *
 * M2 的 `/v1/lots` 是例外：後台要有進貨入口（ADR-032），所以 mock 也要有，
 * 不然「進貨後可用量要變」這件事沒有地方能驗。
 */

import { http, HttpResponse } from 'msw';
import type { components } from '../types.admin';
import { adminAuthHandlers } from './handlers.admin.auth';
import { adminCompensationHandlers } from './handlers.admin.compensation';
import { adminProcurementHandlers } from './handlers.admin.procurement';
import { adminShipmentHandlers } from './handlers.admin.shipments';
import {
  adminCampaignDetailOf,
  adminCampaignOffersByCampaignId,
  adminCampaigns as adminCampaignFixtures,
  adminOrderListItemOf,
  adminOrders as adminOrderFixtures,
  adminProducts as adminProductFixtures,
  campaignMarginFixture,
  categories as categoryFixtures,
  journalEntries,
  liabilityVsCashFixture,
  staffFixture,
} from './fixtures.admin';
import { hexId } from './ids';
import { paginate } from './pagination';
import { problem, jsonProblem } from './problems';

type S = components['schemas'];

export const ADMIN_BASE_URL = 'http://localhost:5001';

function url(path: string): string {
  return `${ADMIN_BASE_URL}${path}`;
}

// ── 可變狀態 ──────────────────────────────────────────────────────────────

let categories: S['Category'][] = categoryFixtures.map((c) => ({ ...c }));
let products: S['AdminProduct'][] = adminProductFixtures.map((p) => ({ ...p, skus: p.skus.map((s) => ({ ...s })) }));
let campaigns: S['AdminCampaign'][] = adminCampaignFixtures.map((c) => ({ ...c }));
let campaignOffers = new Map<string, S['AdminCampaignOffer'][]>(
  Array.from(adminCampaignOffersByCampaignId.entries()).map(([k, v]) => [k, v.map((o) => ({ ...o }))]),
);
let orders: S['AdminOrder'][] = adminOrderFixtures.map((o) => ({ ...o }));
/** 批號（M2）。fixture 沒有種任何一筆——庫存要靠 `POST /v1/lots` 進貨才會有。 */
let lots: S['Lot'][] = [];

/** 測試之間重置 mock 的可變狀態。 */
export function resetAdminMockState(): void {
  categories = categoryFixtures.map((c) => ({ ...c }));
  products = adminProductFixtures.map((p) => ({ ...p, skus: p.skus.map((s) => ({ ...s })) }));
  campaigns = adminCampaignFixtures.map((c) => ({ ...c }));
  campaignOffers = new Map(Array.from(adminCampaignOffersByCampaignId.entries()).map(([k, v]) => [k, v.map((o) => ({ ...o }))]));
  orders = adminOrderFixtures.map((o) => ({ ...o }));
  lots = [];
}

export const adminHandlers = [
  // ── auth ──────────────────────────────────────────────────────────────
  http.post(url('/v1/auth/login'), () => HttpResponse.json(staffFixture)),
  http.post(url('/v1/auth/logout'), () => new HttpResponse(null, { status: 204 })),
  http.get(url('/v1/me'), () => HttpResponse.json(staffFixture)),

  // ── catalog ───────────────────────────────────────────────────────────
  http.get(url('/v1/categories'), () => HttpResponse.json(categories)),

  http.post(url('/v1/categories'), async ({ request }) => {
    const body = (await request.json()) as S['CategoryInput'];
    const created: S['Category'] = { id: hexId(`admin-category:${Date.now()}`), ...body };
    categories = [...categories, created];
    return HttpResponse.json(created, { status: 201 });
  }),

  http.patch(url('/v1/categories/:categoryId'), async ({ request, params }) => {
    const index = categories.findIndex((c) => c.id === params.categoryId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個分類。'));
    const body = (await request.json()) as S['CategoryInput'];
    const current = categories[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個分類。'));
    const updated: S['Category'] = { ...current, ...body };
    categories = categories.map((c, i) => (i === index ? updated : c));
    return HttpResponse.json(updated);
  }),

  http.get(url('/v1/products'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const keyword = q.get('q')?.toLowerCase() ?? undefined;
    const categoryId = q.get('categoryId') ?? undefined;
    const includeArchived = q.get('includeArchived') === 'true';
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = products.filter((p) => {
      if (!includeArchived && !p.isActive) return false;
      if (categoryId && p.categoryId !== categoryId) return false;
      if (keyword && !p.name.toLowerCase().includes(keyword)) return false;
      return true;
    });
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.post(url('/v1/products'), async ({ request }) => {
    const body = (await request.json()) as S['AdminProductInput'];
    const created: S['AdminProduct'] = { id: hexId(`admin-product:${Date.now()}`), skus: [], ...body };
    products = [...products, created];
    return HttpResponse.json(created, { status: 201 });
  }),

  http.get(url('/v1/products/:productId'), ({ params }) => {
    const found = products.find((p) => p.id === params.productId);
    if (!found) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    return HttpResponse.json(found);
  }),

  http.patch(url('/v1/products/:productId'), async ({ request, params }) => {
    const index = products.findIndex((p) => p.id === params.productId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    const current = products[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    const body = (await request.json()) as S['AdminProductInput'];
    const updated: S['AdminProduct'] = { ...current, ...body };
    products = products.map((p, i) => (i === index ? updated : p));
    return HttpResponse.json(updated);
  }),

  http.post(url('/v1/products/:productId/skus'), async ({ request, params }) => {
    const index = products.findIndex((p) => p.id === params.productId);
    const current = products[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個商品。'));
    const body = (await request.json()) as S['AdminSkuInput'];
    if (!body.name || body.weightGram === undefined || body.weightGram === null || !body.size) {
      return jsonProblem(
        problem(422, 'catalog.weight-and-size-required', 'SKU 必須填寫名稱、重量與尺寸', {
          errors: { weightGram: ['重量為必填。'], size: ['尺寸為必填。'] },
        }),
      );
    }
    // `available` 由後端算（批號的 quantityAvailable 加總），新 SKU 一定是 0——前端不准自己算。
    const created: S['AdminSku'] = {
      id: hexId(`admin-sku:${current.id}:${body.name}:${Date.now()}`),
      available: 0,
      variantName: null,
      unitOfMeasure: null,
      unitCount: null,
      listPrice: null,
      ...body,
    };
    products = products.map((p, i) => (i === index ? { ...p, skus: [...p.skus, created] } : p));
    return HttpResponse.json(created, { status: 201 });
  }),

  http.patch(url('/v1/skus/:skuId'), async ({ request, params }) => {
    const body = (await request.json()) as S['AdminSkuInput'];
    let updatedSku: S['AdminSku'] | null = null;
    products = products.map((p) => ({
      ...p,
      skus: p.skus.map((s) => {
        if (s.id !== params.skuId) return s;
        updatedSku = { ...s, ...body };
        return updatedSku;
      }),
    }));
    if (!updatedSku) return jsonProblem(problem(404, 'platform.not-found', '找不到這個 SKU。'));
    return HttpResponse.json(updatedSku);
  }),

  // ── inventory（M2）────────────────────────────────────────────────────
  http.get(url('/v1/lots'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const skuId = q.get('skuId') ?? undefined;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    return HttpResponse.json(paginate(lots.filter((l) => !skuId || l.skuId === skuId), cursor, limit));
  }),

  http.post(url('/v1/lots'), async ({ request }) => {
    const body = (await request.json()) as {
      skuId: string;
      quantity: number;
      unitCost: S['Money'];
      batchCode?: string | null;
    };
    const sku = products.flatMap((p) => p.skus).find((s) => s.id === body.skuId);
    if (!sku) return jsonProblem(problem(422, 'catalog.sku-not-found', '找不到這個 SKU。'));
    if (!Number.isInteger(body.quantity) || body.quantity < 1) {
      return jsonProblem(
        problem(422, 'inventory.quantity-invalid', '進貨數量必須是大於 0 的整數。', {
          errors: { quantity: ['數量必須 ≥ 1。'] },
        }),
      );
    }
    const created: S['Lot'] = {
      id: hexId(`admin-lot:${body.skuId}:${Date.now()}:${lots.length}`),
      skuId: sku.id,
      skuName: sku.name,
      source: 'LocalWholesale',
      unitCost: body.unitCost,
      quantityOnHand: body.quantity,
      quantityReserved: 0,
      quantityAvailable: body.quantity,
      batchCode: body.batchCode ?? null,
      fromCampaignId: null,
      receivedAt: new Date().toISOString(),
    };
    lots = [...lots, created];
    // 可用量是後端的事實，mock 也照著算，這樣「進貨後可用量要變」才驗得到。
    products = products.map((p) => ({
      ...p,
      skus: p.skus.map((s) => (s.id === sku.id ? { ...s, available: s.available + body.quantity } : s)),
    }));
    return HttpResponse.json(created, { status: 201 });
  }),

  // ── campaign ──────────────────────────────────────────────────────────
  http.get(url('/v1/campaigns'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const status = q.get('status') as S['CampaignStatus'] | null;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = campaigns.filter((c) => !status || c.status === status);
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.post(url('/v1/campaigns'), async ({ request }) => {
    const body = (await request.json()) as S['AdminCampaignInput'];
    const created: S['AdminCampaign'] = {
      id: hexId(`admin-campaign:${Date.now()}`),
      status: 'Draft',
      orderCount: 0,
      tripCostTotal: null,
      ...body,
    };
    campaigns = [...campaigns, created];
    campaignOffers.set(created.id, []);
    return HttpResponse.json(created, { status: 201 });
  }),

  http.get(url('/v1/campaigns/:campaignId'), ({ params }) => {
    const detail = adminCampaignDetailFrom(String(params.campaignId));
    if (!detail) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    return HttpResponse.json(detail);
  }),

  http.patch(url('/v1/campaigns/:campaignId'), async ({ request, params }) => {
    const index = campaigns.findIndex((c) => c.id === params.campaignId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    const current = campaigns[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    if (current.status !== 'Draft') {
      return jsonProblem(problem(422, 'campaign.cannot-edit-after-publish', '已發布的團不能再編輯基本資料。'));
    }
    const body = (await request.json()) as S['AdminCampaignInput'];
    const updated: S['AdminCampaign'] = { ...current, ...body };
    campaigns = campaigns.map((c, i) => (i === index ? updated : c));
    return HttpResponse.json(updated);
  }),

  http.post(url('/v1/campaigns/:campaignId/publish'), ({ params }) => transitionCampaign(params.campaignId, 'Draft', 'Open')),
  http.post(url('/v1/campaigns/:campaignId/close'), ({ params }) => transitionCampaign(params.campaignId, 'Open', 'Closed')),

  http.post(url('/v1/campaigns/:campaignId/cancel'), ({ params }) => {
    const index = campaigns.findIndex((c) => c.id === params.campaignId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    const current = campaigns[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    const updated: S['AdminCampaign'] = { ...current, status: 'Cancelled' };
    campaigns = campaigns.map((c, i) => (i === index ? updated : c));
    return HttpResponse.json(updated);
  }),

  http.post(url('/v1/campaigns/:campaignId/settle'), ({ params }) => {
    const index = campaigns.findIndex((c) => c.id === params.campaignId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    const current = campaigns[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    const unshippedOrders = orders.some(
      (o) => o.campaignId === current.id && !['Shipped', 'Completed', 'Cancelled'].includes(o.status),
    );
    if (unshippedOrders) {
      return jsonProblem(problem(422, 'campaign.orders-not-all-shipped', '這個團還有訂單尚未出貨，不能結團。'));
    }
    const updated: S['AdminCampaign'] = { ...current, status: 'Settled' };
    campaigns = campaigns.map((c, i) => (i === index ? updated : c));
    return HttpResponse.json(updated);
  }),

  http.get(url('/v1/campaigns/:campaignId/offers'), ({ params }) =>
    HttpResponse.json(campaignOffers.get(String(params.campaignId)) ?? []),
  ),

  http.post(url('/v1/campaigns/:campaignId/offers'), async ({ request, params }) => {
    const campaignId = String(params.campaignId);
    if (!campaigns.some((c) => c.id === campaignId)) {
      return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
    }
    const body = (await request.json()) as {
      skuId: string;
      sellingPrice: S['Money'];
      targetPurchasePrice?: S['Money'] | null;
    };
    const sku = products.flatMap((p) => p.skus).find((s) => s.id === body.skuId);
    if (!sku) return jsonProblem(problem(422, 'catalog.sku-not-found', '找不到這個 SKU。'));
    const created: S['AdminCampaignOffer'] = {
      id: hexId(`admin-offer:${campaignId}:${body.skuId}:${Date.now()}`),
      skuId: sku.id,
      name: sku.name,
      variantName: sku.variantName ?? null,
      sellingPrice: body.sellingPrice,
      targetPurchasePrice: body.targetPurchasePrice ?? null,
      isActive: true,
      orderedQuantity: 0,
    };
    campaignOffers.set(campaignId, [...(campaignOffers.get(campaignId) ?? []), created]);
    return HttpResponse.json(created, { status: 201 });
  }),

  http.delete(url('/v1/campaigns/:campaignId/offers/:offerId'), ({ params }) => {
    const campaignId = String(params.campaignId);
    const offers = campaignOffers.get(campaignId) ?? [];
    const target = offers.find((o) => o.id === params.offerId);
    if (!target) return jsonProblem(problem(404, 'platform.not-found', '找不到這個開團商品。'));
    if (target.orderedQuantity > 0) {
      return jsonProblem(problem(422, 'campaign.offer-has-orders', '已經有訂單引用這個商品，不能移除，請改成停用。'));
    }
    campaignOffers.set(campaignId, offers.filter((o) => o.id !== params.offerId));
    return new HttpResponse(null, { status: 204 });
  }),

  // ── order ─────────────────────────────────────────────────────────────
  http.get(url('/v1/orders'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const keyword = q.get('q')?.toLowerCase() ?? undefined;
    const status = q.get('status') as S['OrderStatus'] | null;
    const campaignId = q.get('campaignId') ?? undefined;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = orders
      .filter((o) => !status || o.status === status)
      .filter((o) => !campaignId || o.campaignId === campaignId)
      .filter(
        (o) =>
          !keyword ||
          o.orderNumber.toLowerCase().includes(keyword) ||
          o.customerDisplayName.toLowerCase().includes(keyword),
      )
      .map(adminOrderListItemOf);
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.get(url('/v1/orders/:orderId'), ({ params }) => {
    const found = orders.find((o) => o.id === params.orderId);
    if (!found) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    return HttpResponse.json(found);
  }),

  http.post(url('/v1/orders/:orderId/cancel'), ({ params }) => {
    const index = orders.findIndex((o) => o.id === params.orderId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    const current = orders[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    const updated: S['AdminOrder'] = {
      ...current,
      status: 'Cancelled',
      lines: current.lines.map((l) => ({ ...l, status: 'Cancelled' as const })),
    };
    orders = orders.map((o, i) => (i === index ? updated : o));
    return HttpResponse.json(updated);
  }),

  http.post(url('/v1/orders/:orderId/lines/:lineId/cancel'), ({ params }) => {
    const index = orders.findIndex((o) => o.id === params.orderId);
    if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    const current = orders[index];
    if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這張訂單。'));
    const lineIndex = current.lines.findIndex((l) => l.id === params.lineId);
    if (lineIndex === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個品項。'));
    const targetLine = current.lines[lineIndex];
    if (!targetLine) return jsonProblem(problem(404, 'platform.not-found', '找不到這個品項。'));
    const updatedLines = current.lines.map((l, i) => (i === lineIndex ? { ...l, status: 'Unavailable' as const } : l));
    const updated: S['AdminOrder'] = { ...current, lines: updatedLines };
    orders = orders.map((o, i) => (i === index ? updated : o));
    return HttpResponse.json(updated);
  }),

  // ── ledger ────────────────────────────────────────────────────────────
  http.get(url('/v1/ledger/entries'), ({ request }) => {
    const q = new URL(request.url).searchParams;
    const sourceModule = q.get('sourceModule') ?? undefined;
    const sourceRef = q.get('sourceRef') ?? undefined;
    const cursor = q.get('cursor') ?? undefined;
    const limit = q.get('limit') ? Number(q.get('limit')) : undefined;
    const filtered = journalEntries.filter(
      (e) => (!sourceModule || e.sourceModule === sourceModule) && (!sourceRef || e.sourceRef === sourceRef),
    );
    return HttpResponse.json(paginate(filtered, cursor, limit));
  }),

  http.get(url('/v1/ledger/campaign-margin/:campaignId'), () => HttpResponse.json(campaignMarginFixture)),

  http.get(url('/v1/ledger/liability-vs-cash'), () => HttpResponse.json(liabilityVsCashFixture)),

  ...adminAuthHandlers,
  ...adminProcurementHandlers,
  ...adminCompensationHandlers,
  ...adminShipmentHandlers,
];

function adminCampaignDetailFrom(campaignId: string): S['AdminCampaignDetail'] | null {
  const campaign = campaigns.find((c) => c.id === campaignId);
  if (!campaign) return null;
  return { ...campaign, offers: campaignOffers.get(campaignId) ?? [] };
}

function transitionCampaign(
  campaignId: unknown,
  from: S['CampaignStatus'],
  to: S['CampaignStatus'],
): Response {
  const index = campaigns.findIndex((c) => c.id === campaignId);
  if (index === -1) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
  const current = campaigns[index];
  if (!current) return jsonProblem(problem(404, 'platform.not-found', '找不到這個團。'));
  if (current.status !== from) {
    return jsonProblem(problem(422, 'campaign.invalid-status-transition', `這個團目前的狀態不能執行這個操作。`));
  }
  const updated: S['AdminCampaign'] = { ...current, status: to };
  campaigns = campaigns.map((c, i) => (i === index ? updated : c));
  return HttpResponse.json(updated);
}

/** 未在成功清單覆蓋、但驗收要看到的錯誤情境。 */
export const adminErrorScenarios = {
  productValidation: http.post(url('/v1/products'), () =>
    jsonProblem(
      problem(422, 'catalog.weight-and-size-required', '商品必須填寫重量與尺寸', {
        errors: { weightGram: ['重量為必填。'], size: ['尺寸為必填。'] },
      }),
    ),
  ),
  skuValidation: http.post(url('/v1/products/:productId/skus'), () =>
    jsonProblem(
      problem(422, 'catalog.weight-and-size-required', 'SKU 必須填寫重量與尺寸', {
        errors: { weightGram: ['重量為必填。'], size: ['尺寸為必填。'] },
      }),
    ),
  ),
  campaignSettleNotAllShipped: http.post(url('/v1/campaigns/:campaignId/settle'), () =>
    jsonProblem(problem(422, 'campaign.orders-not-all-shipped', '這個團還有訂單尚未出貨，不能結團。')),
  ),
  offerHasOrdersConflict: http.delete(url('/v1/campaigns/:campaignId/offers/:offerId'), () =>
    jsonProblem(problem(422, 'campaign.offer-has-orders', '已經有訂單引用這個商品，不能移除。')),
  ),
  forbidden: http.post(url('/v1/campaigns/:campaignId/cancel'), () =>
    jsonProblem(problem(403, 'platform.forbidden', '你的角色沒有權限執行這個操作。')),
  ),
  serverError: http.get(url('/v1/orders'), () => jsonProblem(problem(500, 'platform.unexpected', '系統發生問題，請稍後再試。'))),
};
