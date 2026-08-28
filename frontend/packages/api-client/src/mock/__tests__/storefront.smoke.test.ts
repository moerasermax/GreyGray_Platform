import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { ApiClient } from '../../http';
import { ApiError } from '../../problem';
import * as api from '../../endpoints/storefront';
import { STOREFRONT_BASE_URL, resetStorefrontMockState, storefrontErrorScenarios } from '../handlers.storefront';
import { storefrontServer } from '../server';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  storefrontServer.resetHandlers();
  resetStorefrontMockState();
});
afterAll(() => storefrontServer.close());

describe('storefront mock：每個 M1a 端點打一次，回應要通過型別檢查', () => {
  it('categories', async () => {
    const categories = await api.listCategories(client);
    expect(categories.length).toBeGreaterThan(0);
    expect(categories[0]).toHaveProperty('name');
  });

  it('products 列表分頁：能翻到第二頁，最後一頁 nextCursor 是 null', async () => {
    const first = await api.listProducts(client, { limit: 20 });
    expect(first.items).toHaveLength(20);
    expect(first.nextCursor).not.toBeNull();

    const second = await api.listProducts(client, { limit: 20, cursor: first.nextCursor! });
    expect(second.items.length).toBeGreaterThan(0);
    expect(second.nextCursor).toBeNull();
  });

  it('product 詳情含 SKU', async () => {
    const list = await api.listProducts(client, { limit: 1 });
    const first = list.items[0];
    expect(first).toBeDefined();
    const detail = await api.getProduct(client, first!.id);
    expect(detail.skus.length).toBeGreaterThan(0);
  });

  it('campaigns 預設只回 Open', async () => {
    const page = await api.listCampaigns(client);
    for (const c of page.items) expect(c.status).toBe('Open');
  });

  it('campaign 詳情含 offers', async () => {
    const page = await api.listCampaigns(client);
    const first = page.items[0];
    expect(first).toBeDefined();
    const detail = await api.getCampaign(client, first!.id);
    expect(Array.isArray(detail.offers)).toBe(true);
  });

  it('購物車：加入 → 改數量 → 刪除', async () => {
    const before = await api.getCart(client);
    const lineCountBefore = before.lines.length;

    const products = await api.listProducts(client, { mode: 'Stock', limit: 1 });
    const product = products.items[0];
    expect(product).toBeDefined();
    const detail = await api.getProduct(client, product!.id);
    const sku = detail.skus[0];
    expect(sku).toBeDefined();

    const afterAdd = await api.addCartLine(client, { skuId: sku!.id, mode: 'Stock', quantity: 3 });
    expect(afterAdd.lines).toHaveLength(lineCountBefore + 1);

    const newLine = afterAdd.lines.at(-1);
    expect(newLine).toBeDefined();
    const afterUpdate = await api.updateCartLine(client, newLine!.id, { quantity: 5 });
    const updatedLine = afterUpdate.lines.find((l) => l.id === newLine!.id);
    expect(updatedLine?.quantity).toBe(5);

    const afterRemove = await api.removeCartLine(client, newLine!.id);
    expect(afterRemove.lines.find((l) => l.id === newLine!.id)).toBeUndefined();
  });

  it('詢價：含運總額 = 商品總額 + 運費', async () => {
    const quote = await api.quoteCart(client, { deliveryMethod: 'ConvenienceStore' });
    expect(quote.grandTotal.amountMinor).toBe(quote.goodsTotal.amountMinor + quote.shippingFee.amountMinor);
    expect(quote.explain.length).toBeGreaterThan(0);
  });

  it('結帳：混合訂單需要 shippingPolicy，成立後訂單狀態是 AwaitingPayment', async () => {
    const order = await api.checkout(client, {
      deliveryMethod: 'ConvenienceStore',
      shippingPolicy: 'ShipSeparately',
      convenienceStoreCode: '991234',
    });
    expect(order.status).toBe('AwaitingPayment');
    expect(order.lines.length).toBeGreaterThan(0);

    const cartAfter = await api.getCart(client);
    expect(cartAfter.lines).toHaveLength(0);
  });

  it('訂單列表與詳情、取消、付款導轉', async () => {
    const order = await api.checkout(client, {
      deliveryMethod: 'SelfPickup',
      shippingPolicy: 'ShipSeparately',
    });

    const list = await api.listOrders(client);
    expect(list.items.some((o) => o.id === order.id)).toBe(true);

    const detail = await api.getOrder(client, order.id);
    expect(detail.id).toBe(order.id);

    const payment = await api.initiatePayment(client, order.id);
    expect(payment.method).toBe('POST');
    expect(payment.fields['MerchantTradeNo']).toBe(order.orderNumber);

    const cancelled = await api.cancelOrder(client, order.id);
    expect(cancelled.status).toBe('Cancelled');
  });

  it('地址 CRUD', async () => {
    const created = await api.createAddress(client, {
      recipientName: '測試客人',
      phoneNumber: '0987654321',
      postalCode: '100',
      city: '台北市',
      district: '中正區',
      streetAddress: '測試路 1 號',
      isDefault: false,
    });
    expect(created.id).toBeTruthy();

    const updated = await api.updateAddress(client, created.id, {
      recipientName: '測試客人改名',
      phoneNumber: '0987654321',
      postalCode: '100',
      city: '台北市',
      district: '中正區',
      streetAddress: '測試路 1 號',
      isDefault: false,
    });
    expect(updated.recipientName).toBe('測試客人改名');

    await api.deleteAddress(client, created.id);
    const list = await api.listAddresses(client);
    expect(list.find((a) => a.id === created.id)).toBeUndefined();
  });

  it('九個訂單狀態 fixture 都能查得到', async () => {
    resetStorefrontMockState();
    const statuses: string[] = [];
    let cursor: string | undefined;
    for (let guard = 0; guard < 10; guard += 1) {
      const page = await api.listOrders(client, cursor ? { cursor, limit: 5 } : { limit: 5 });
      statuses.push(...page.items.map((o) => o.status));
      if (!page.nextCursor) break;
      cursor = page.nextCursor;
    }
    for (const s of [
      'AwaitingPayment',
      'PaidAwaitingClose',
      'ClosedAwaitingDeparture',
      'Purchasing',
      'GoodsReceived',
      'ReadyToShip',
      'Shipped',
      'Completed',
      'Cancelled',
    ]) {
      expect(statuses).toContain(s);
    }
  });
});

describe('storefront mock：錯誤情境', () => {
  beforeEach(() => resetStorefrontMockState());

  it('422：註冊密碼太弱時 ApiError.fieldErrors 有值', async () => {
    storefrontServer.use(storefrontErrorScenarios.registerWeakPassword);
    expect.assertions(3);
    try {
      await api.register(client, { phoneNumber: '0912345678', password: '123', displayName: '測試' });
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(422);
      expect((error as ApiError).fieldErrors['password']).toBeDefined();
    }
  });

  it('409：訂單已付款時再次取得付款參數', async () => {
    storefrontServer.use(storefrontErrorScenarios.paymentAlreadyPaid);
    expect.assertions(3);
    try {
      await api.initiatePayment(client, 'irrelevant');
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(409);
      expect((error as ApiError).code).toBe('ordering.order-already-paid');
    }
  });

  it('500：伺服器錯誤時回傳系統訊息，不吐內部細節', async () => {
    storefrontServer.use(storefrontErrorScenarios.serverError);
    expect.assertions(3);
    try {
      await api.listProducts(client);
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).status).toBe(500);
      expect((error as ApiError).problem.title).toBe('系統發生問題，請稍後再試。');
    }
  });
});
