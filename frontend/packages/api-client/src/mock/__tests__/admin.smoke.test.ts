import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { ApiClient } from '../../http';
import { ApiError } from '../../problem';
import * as api from '../../endpoints/admin';
import { ADMIN_BASE_URL, adminErrorScenarios, resetAdminMockState } from '../handlers.admin';
import { adminServer } from '../server';

const client = new ApiClient({ baseUrl: ADMIN_BASE_URL });
let mutationSequence = 0;
const mutationOptions = () => ({ idempotencyKey: `admin-smoke-${++mutationSequence}` });

beforeAll(() => adminServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  adminServer.resetHandlers();
  resetAdminMockState();
});
afterAll(() => adminServer.close());

describe('admin mock：每個 M1a 端點打一次，回應要通過型別檢查', () => {
  it('登入與 me', async () => {
    const staff = await api.login(client, { email: 'owner@greygray.tw', password: 'password123' }, mutationOptions());
    expect(staff.role).toBeTruthy();
    const me = await api.getMe(client);
    expect(me.id).toBe(staff.id);
  });

  it('分類 CRUD', async () => {
    const created = await api.createCategory(client, { name: '新分類', imageUrl: null, sortOrder: 9 }, mutationOptions());
    expect(created.id).toBeTruthy();
    const updated = await api.updateCategory(client, created.id, { name: '改名分類', imageUrl: null, sortOrder: 9 }, mutationOptions());
    expect(updated.name).toBe('改名分類');
    const list = await api.listCategories(client);
    expect(list.some((c) => c.id === created.id)).toBe(true);
  });

  it('商品與 SKU', async () => {
    const page = await api.listProducts(client);
    expect(page.items.length).toBeGreaterThan(0);
    const first = page.items[0];
    expect(first).toBeDefined();
    const detail = await api.getProduct(client, first!.id);
    expect(detail.skus.length).toBeGreaterThan(0);

    const sku = detail.skus[0];
    expect(sku).toBeDefined();
    const updatedSku = await api.updateSku(client, sku!.id, {
      name: sku!.name,
      weightGram: 999,
      size: sku!.size,
      isActive: true,
    }, mutationOptions());
    expect(updatedSku.weightGram).toBe(999);
  });

  it('新增 SKU（ADR-032）：建立後 getProduct 就看得到，可用量是 0', async () => {
    const page = await api.listProducts(client);
    const product = page.items[0];
    expect(product).toBeDefined();
    const before = product!.skus.length;

    const created = await api.createSku(client, product!.id, {
      name: '新 SKU 測試',
      variantName: '30 入',
      weightGram: 250,
      size: { lengthCm: 10, widthCm: 8, heightCm: 5 },
      listPrice: { amountMinor: 18000, currency: 'TWD' },
      isActive: true,
    }, mutationOptions());

    expect(created.id).toBeTruthy();
    // 可用量是後端算的（批號加總），新 SKU 一定是 0——前端不准自己算。
    expect(created.available).toBe(0);

    const detail = await api.getProduct(client, product!.id);
    expect(detail.skus).toHaveLength(before + 1);
    expect(detail.skus.some((s) => s.id === created.id && s.name === '新 SKU 測試')).toBe(true);
  });

  it('新增 SKU：422 帶得回逐欄位的錯誤', async () => {
    adminServer.use(adminErrorScenarios.skuValidation);
    const page = await api.listProducts(client);
    const product = page.items[0];

    const error = await api
      .createSku(client, product!.id, {
        name: '缺重量',
        weightGram: 0,
        size: { lengthCm: 0, widthCm: 0, heightCm: 0 },
        isActive: true,
      }, mutationOptions())
      .catch((cause: unknown) => cause);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).problem.status).toBe(422);
    expect((error as ApiError).problem.code).toBe('catalog.weight-and-size-required');
  });

  it('進貨（M2）：createLot 後 listLots 看得到，SKU 可用量跟著加', async () => {
    const page = await api.listProducts(client);
    const product = page.items[0];
    const sku = product!.skus[0];
    expect(sku).toBeDefined();

    const emptyAtFirst = await api.listLots(client, { skuId: sku!.id });
    expect(emptyAtFirst.items).toEqual([]);

    const lot = await api.createLot(client, {
      skuId: sku!.id,
      quantity: 10,
      unitCost: { amountMinor: 12000, currency: 'TWD' },
      batchCode: 'B-2026-09',
    }, mutationOptions());

    expect(lot.quantityAvailable).toBe(10);
    expect(lot.batchCode).toBe('B-2026-09');

    const afterLots = await api.listLots(client, { skuId: sku!.id });
    expect(afterLots.items.map((l) => l.id)).toEqual([lot.id]);

    const detail = await api.getProduct(client, product!.id);
    expect(detail.skus.find((s) => s.id === sku!.id)?.available).toBe(sku!.available + 10);
  });

  it('進貨：數量 < 1 是 422，不會產生批號', async () => {
    const page = await api.listProducts(client);
    const sku = page.items[0]!.skus[0];

    const error = await api
      .createLot(client, { skuId: sku!.id, quantity: 0, unitCost: { amountMinor: 100, currency: 'TWD' } }, mutationOptions())
      .catch((cause: unknown) => cause);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).problem.status).toBe(422);
    expect((await api.listLots(client, { skuId: sku!.id })).items).toEqual([]);
  });

  it('開團：建立 → 加商品 → 發布 → 截團', async () => {
    const campaign = await api.createCampaign(client, {
      title: '測試團',
      destination: '測試地',
      departAt: '2026-12-01',
      returnAt: '2026-12-05',
      closesAt: '2026-11-25T00:00:00+08:00',
    }, mutationOptions());
    expect(campaign.status).toBe('Draft');

    const products = await api.listProducts(client);
    const sku = products.items[0]?.skus[0];
    expect(sku).toBeDefined();

    const offer = await api.addCampaignOffer(client, campaign.id, {
      skuId: sku!.id,
      sellingPrice: { amountMinor: 50000, currency: 'TWD' },
    }, mutationOptions());
    expect(offer.id).toBeTruthy();

    const published = await api.publishCampaign(client, campaign.id, mutationOptions());
    expect(published.status).toBe('Open');

    const closed = await api.closeCampaign(client, campaign.id, mutationOptions());
    expect(closed.status).toBe('Closed');
  });

  it('已發布的團，改基本資料回 422（cannot-edit-after-publish）', async () => {
    const page = await api.listCampaigns(client, { status: 'Open' });
    const openCampaign = page.items[0];
    expect(openCampaign).toBeDefined();

    expect.assertions(3);
    try {
      await api.updateCampaign(client, openCampaign!.id, {
        title: '改標題',
        destination: openCampaign!.destination,
        departAt: openCampaign!.departAt,
        returnAt: openCampaign!.returnAt,
        closesAt: openCampaign!.closesAt,
      }, mutationOptions());
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).code).toBe('campaign.cannot-edit-after-publish');
    }
  });

  it('尚有訂單未出貨時結團回 422（orders-not-all-shipped）', async () => {
    const page = await api.listCampaigns(client);
    const campaign = page.items[0];
    expect(campaign).toBeDefined();
    adminServer.use(adminErrorScenarios.campaignSettleNotAllShipped);

    expect.assertions(4);
    try {
      await api.settleCampaign(client, campaign!.id, mutationOptions());
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      expect((error as ApiError).code).toBe('campaign.orders-not-all-shipped');
    }
  });

  it('訂單列表含混合三種 line 狀態的那張單', async () => {
    const page = await api.listOrders(client);
    const mixed = page.items.find((o) => o.status === 'Purchasing');
    expect(mixed).toBeDefined();
    const detail = await api.getOrder(client, mixed!.id);
    const statuses = new Set(detail.lines.map((l) => l.status));
    expect(statuses.has('Shipped')).toBe(true);
    expect(statuses.has('Unavailable')).toBe(true);
    expect(statuses.has('Pending')).toBe(true);
    expect(detail.customerContactMasked).toMatch(/\*/);
  });

  it('取消單一品項', async () => {
    const page = await api.listOrders(client, { status: 'AwaitingPayment' });
    const order = page.items[0];
    expect(order).toBeDefined();
    const detail = await api.getOrder(client, order!.id);
    const line = detail.lines[0];
    expect(line).toBeDefined();
    const updated = await api.cancelOrderLine(client, order!.id, line!.id, {
      reason: '客人反悔',
      refundTo: 'StoredValue',
    }, mutationOptions());
    expect(updated.lines.find((l) => l.id === line!.id)?.status).toBe('Unavailable');
  });

  it('分錄借貸兩欄合計相等', async () => {
    const page = await api.listLedgerEntries(client);
    let debit = 0;
    let credit = 0;
    for (const entry of page.items) {
      for (const line of entry.lines) {
        if (line.direction === 'Debit') debit += line.amount.amountMinor;
        else credit += line.amount.amountMinor;
      }
    }
    expect(debit).toBe(credit);
  });

  it('負債現金比：isBreached 為 true 時要能看得到', async () => {
    const result = await api.getLiabilityVsCash(client);
    expect(result.isBreached).toBe(true);
  });

  it('每團毛利', async () => {
    const page = await api.listCampaigns(client, { status: 'Open' });
    const campaign = page.items[0];
    expect(campaign).toBeDefined();
    const margin = await api.getCampaignMargin(client, campaign!.id);
    expect(margin.grossMargin).toBeDefined();
  });
});

describe('admin mock：錯誤情境', () => {
  beforeEach(() => resetAdminMockState());

  it('422：建立商品缺重量與尺寸', async () => {
    adminServer.use(adminErrorScenarios.productValidation);
    expect.assertions(3);
    try {
      await api.createProduct(client, { name: '缺尺寸的商品', mode: 'Stock', isActive: true }, mutationOptions());
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      expect((error as ApiError).fieldErrors['weightGram']).toBeDefined();
    }
  });

  it('403：角色不足時取消團被擋下', async () => {
    adminServer.use(adminErrorScenarios.forbidden);
    expect.assertions(2);
    try {
      await api.cancelCampaign(client, 'irrelevant', { reason: '測試' }, mutationOptions());
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(403);
    }
  });

  it('500：訂單列表伺服器錯誤', async () => {
    adminServer.use(adminErrorScenarios.serverError);
    expect.assertions(2);
    try {
      await api.listOrders(client);
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(500);
    }
  });
});
