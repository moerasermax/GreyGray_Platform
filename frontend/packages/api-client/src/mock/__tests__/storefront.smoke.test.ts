import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { ApiClient } from '../../http';
import { ApiError } from '../../problem';
import * as api from '../../endpoints/storefront';
import { STOREFRONT_BASE_URL, resetStorefrontMockState, storefrontErrorScenarios } from '../handlers.storefront';
import { storefrontServer } from '../server';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });
let mutationSequence = 0;
const mutationOptions = () => ({ idempotencyKey: `storefront-smoke-${++mutationSequence}` });

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

  it('最愛列表：最新收藏在前、能翻到末頁，且未定價商品仍會出現', async () => {
    const first = await api.listFavorites(client, { limit: 1 });
    expect(first.items).toHaveLength(1);
    expect(first.items[0]?.priceFrom).toBeNull();
    expect(first.items[0]?.isFavorited).toBe(true);
    expect(first.nextCursor).not.toBeNull();

    const second = await api.listFavorites(client, { cursor: first.nextCursor!, limit: 100 });
    expect(second.items.length).toBeGreaterThan(0);
    expect(second.nextCursor).toBeNull();
  });

  it('最愛 PUT 重放只留一筆；DELETE 對不存在的收藏仍回 204', async () => {
    const products = await api.listProducts(client, { limit: 100 });
    const target = products.items.find((product) => !product.isFavorited);
    expect(target).toBeDefined();
    const replayOptions = { idempotencyKey: 'favorite-put-replay' };

    await api.addFavorite(client, target!.id, replayOptions);
    await api.addFavorite(client, target!.id, replayOptions);
    const afterPut = await api.listFavorites(client, { limit: 100 });
    expect(afterPut.items.filter((product) => product.id === target!.id)).toHaveLength(1);
    expect(afterPut.items[0]?.id).toBe(target!.id);

    await api.removeFavorite(client, target!.id, { idempotencyKey: 'favorite-delete-once' });
    await api.removeFavorite(client, target!.id, { idempotencyKey: 'favorite-delete-replay' });
    const afterDelete = await api.listFavorites(client, { limit: 100 });
    expect(afterDelete.items.some((product) => product.id === target!.id)).toBe(false);
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

    const afterAdd = await api.addCartLine(client, { skuId: sku!.id, mode: 'Stock', quantity: 3 }, mutationOptions());
    expect(afterAdd.lines).toHaveLength(lineCountBefore + 1);

    const newLine = afterAdd.lines.at(-1);
    expect(newLine).toBeDefined();
    const afterUpdate = await api.updateCartLine(client, newLine!.id, { quantity: 5 }, mutationOptions());
    const updatedLine = afterUpdate.lines.find((l) => l.id === newLine!.id);
    expect(updatedLine?.quantity).toBe(5);

    const afterRemove = await api.removeCartLine(client, newLine!.id, mutationOptions());
    expect(afterRemove.lines.find((l) => l.id === newLine!.id)).toBeUndefined();
  });

  it('詢價：含運總額 = 商品總額 + 運費', async () => {
    const quote = await api.quoteCart(client, { deliveryMethod: 'ConvenienceStore' }, mutationOptions());
    expect(quote.grandTotal.amountMinor).toBe(quote.goodsTotal.amountMinor + quote.shippingFee.amountMinor);
    expect(quote.explain.length).toBeGreaterThan(0);
  });

  it('結帳：混合訂單需要 shippingPolicy，成立後訂單狀態是 AwaitingPayment', async () => {
    const order = await api.checkout(client, {
      deliveryMethod: 'ConvenienceStore',
      shippingPolicy: 'ShipSeparately',
      convenienceStoreCode: '991234',
      recipientName: '王小美',
      recipientPhone: '0912345678',
    }, mutationOptions());
    expect(order.status).toBe('AwaitingPayment');
    expect(order.lines.length).toBeGreaterThan(0);
    expect(order.recipientName).toBe('王小美');
    expect(order.recipientPhone).toBe('0912345678');

    const cartAfter = await api.getCart(client);
    expect(cartAfter.lines).toHaveLength(0);
  });

  it('超商取貨（ADR-038）：開票 → 讀票 → 帶票結帳，訂單有門市名稱與地址', async () => {
    const session = await api.createCvsMapSession(client, {});
    expect(session.method).toBe('POST');
    expect(session.selectionId).toMatch(/^[A-Za-z0-9]{20}$/);
    expect(session.fields['ExtraData']).toBe(session.selectionId);

    const selection = await api.getCvsSelection(client, session.selectionId);
    expect(selection.selectionId).toBe(session.selectionId);
    expect(selection.storeName).toBeTruthy();
    expect(selection.storeAddress).toBeTruthy();

    const order = await api.checkout(client, {
      deliveryMethod: 'ConvenienceStore',
      shippingPolicy: 'ShipSeparately',
      convenienceStoreSelectionId: session.selectionId,
      recipientName: '王小美',
      recipientPhone: '0912345678',
    }, mutationOptions());
    expect(order.convenienceStoreName).toBe(selection.storeName);
    expect(order.convenienceStoreAddress).toBe(selection.storeAddress);
    expect(order.recipientName).toBe('王小美');
    expect(order.recipientPhone).toBe('0912345678');
  });

  it('訂單列表與詳情、取消、付款導轉', async () => {
    const order = await api.checkout(client, {
      deliveryMethod: 'SelfPickup',
      shippingPolicy: 'ShipSeparately',
    }, mutationOptions());

    const list = await api.listOrders(client);
    expect(list.items.some((o) => o.id === order.id)).toBe(true);

    const detail = await api.getOrder(client, order.id);
    expect(detail.id).toBe(order.id);

    const payment = await api.initiatePayment(client, order.id, mutationOptions());
    expect(payment.method).toBe('POST');
    expect(payment.fields['MerchantTradeNo']).toBe(order.orderNumber);

    const cancelled = await api.cancelOrder(client, order.id, {}, mutationOptions());
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
    }, mutationOptions());
    expect(created.id).toBeTruthy();

    const updated = await api.updateAddress(client, created.id, {
      recipientName: '測試客人改名',
      phoneNumber: '0987654321',
      postalCode: '100',
      city: '台北市',
      district: '中正區',
      streetAddress: '測試路 1 號',
      isDefault: false,
    }, mutationOptions());
    expect(updated.recipientName).toBe('測試客人改名');

    await api.deleteAddress(client, created.id, mutationOptions());
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

describe('logistics 端點（ADR-038）：路徑與方法', () => {
  it('createCvsMapSession 是 POST、不帶冪等鍵；getCvsSelection 是 GET', async () => {
    const calls: Array<[string, string, unknown]> = [];
    const recorder = {
      post: (path: string, options: unknown) => {
        calls.push(['POST', path, options]);
        return Promise.resolve({});
      },
      get: (path: string, options: unknown) => {
        calls.push(['GET', path, options]);
        return Promise.resolve({});
      },
    } as unknown as ApiClient;

    await api.createCvsMapSession(recorder, {});
    await api.getCvsSelection(recorder, 'ABCDEFGHIJ0123456789');

    expect(calls).toEqual([
      ['POST', '/v1/logistics/cvs-map-sessions', { body: {} }],
      ['GET', '/v1/logistics/cvs-selections/ABCDEFGHIJ0123456789', {}],
    ]);
  });
});

describe('storefront mock：錯誤情境', () => {
  beforeEach(() => resetStorefrontMockState());

  it('503：沒有物流設定時開票回 logistics.not-configured', async () => {
    storefrontServer.use(storefrontErrorScenarios.cvsMapNotConfigured);
    await expect(api.createCvsMapSession(client, {})).rejects.toMatchObject({
      status: 503,
      problem: { code: 'logistics.not-configured' },
    });
  });

  it('404：沒開過的選店票讀不到', async () => {
    await expect(api.getCvsSelection(client, 'ZZZZZZZZZZZZZZZZZZZZ')).rejects.toMatchObject({ status: 404 });
  });

  it('422：帶沒開過的選店票結帳回 checkout.store-selection-expired', async () => {
    await expect(
      api.checkout(client, {
        deliveryMethod: 'ConvenienceStore',
        shippingPolicy: 'ShipSeparately',
        convenienceStoreSelectionId: 'ZZZZZZZZZZZZZZZZZZZZ',
        recipientName: '王小美',
        recipientPhone: '0912345678',
      }, mutationOptions()),
    ).rejects.toMatchObject({ status: 422, problem: { code: 'checkout.store-selection-expired' } });
  });

  it('422：超商取貨沒填收件人姓名手機回 checkout.recipient-required（ADR-039）', async () => {
    await expect(
      api.checkout(client, {
        deliveryMethod: 'ConvenienceStore',
        shippingPolicy: 'ShipSeparately',
        convenienceStoreCode: '991234',
      }, mutationOptions()),
    ).rejects.toMatchObject({ status: 422, problem: { code: 'checkout.recipient-required' } });
  });

  it('422：註冊密碼太弱時 ApiError.fieldErrors 有值', async () => {
    storefrontServer.use(storefrontErrorScenarios.registerWeakPassword);
    expect.assertions(3);
    try {
      await api.register(client, { phoneNumber: '0912345678', password: '123', displayName: '測試' }, mutationOptions());
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
      await api.initiatePayment(client, 'irrelevant', mutationOptions());
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

  it('404：不存在的商品不能加入最愛', async () => {
    await expect(
      api.addFavorite(client, '00000000-0000-0000-0000-000000000000', mutationOptions()),
    ).rejects.toMatchObject({ status: 404 });
  });

  it('DELETE 最愛：合法但不存在的 id 仍 204，格式不合法才 404', async () => {
    await expect(
      api.removeFavorite(client, '00000000000000000000000000000000', mutationOptions()),
    ).resolves.toBeUndefined();
    await expect(api.removeFavorite(client, 'not-an-id', mutationOptions())).rejects.toMatchObject({
      status: 404,
    });
  });

  it('422：最愛列表拒絕壞 cursor 與超出 1 到 100 的 limit', async () => {
    await expect(api.listFavorites(client, { cursor: 'not-a-cursor' })).rejects.toMatchObject({
      status: 422,
      code: 'catalog.invalid-cursor',
    });
    await expect(api.listFavorites(client, { limit: 101 })).rejects.toMatchObject({
      status: 422,
      code: 'catalog.invalid-cursor',
    });
  });
});
