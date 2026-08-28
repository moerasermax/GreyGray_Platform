/** 驗收條件：切換超商／宅配／自取，含運總額跟著變，且都是後端回的數字。 */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { ApiClient } from '@greygray/api-client';
import * as api from '@greygray/api-client/endpoints/storefront';
import { STOREFRONT_BASE_URL, resetStorefrontMockState } from '@greygray/api-client/mock/handlers.storefront';
import { storefrontServer } from '@greygray/api-client/mock/server';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });
let mutationSequence = 0;
const mutationOptions = () => ({ idempotencyKey: `quote-test-${++mutationSequence}` });

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  storefrontServer.resetHandlers();
  resetStorefrontMockState();
});
afterAll(() => storefrontServer.close());

describe('切換配送方式重新詢價', () => {
  it('超商／宅配／自取的含運總額不同，且 grandTotal 就是後端回的 goodsTotal + shippingFee', async () => {
    const convenience = await api.quoteCart(client, { deliveryMethod: 'ConvenienceStore' }, mutationOptions());
    const home = await api.quoteCart(client, { deliveryMethod: 'HomeDelivery' }, mutationOptions());
    const pickup = await api.quoteCart(client, { deliveryMethod: 'SelfPickup' }, mutationOptions());

    for (const quote of [convenience, home, pickup]) {
      expect(quote.grandTotal.amountMinor).toBe(
        quote.goodsTotal.amountMinor + quote.shippingFee.amountMinor,
      );
      expect(quote.explain.length).toBeGreaterThan(0);
    }

    expect(pickup.shippingFee.amountMinor).toBe(0);
    expect(convenience.shippingFee.amountMinor).toBeGreaterThan(0);
    expect(home.shippingFee.amountMinor).toBeGreaterThan(convenience.shippingFee.amountMinor);

    const totals = new Set(
      [convenience, home, pickup].map((q) => q.grandTotal.amountMinor),
    );
    expect(totals.size).toBe(3); // 三種配送方式的含運總額互不相同
  });

  it('購物車是空的時候詢價回 422 checkout.cart-empty', async () => {
    const cart = await api.getCart(client);
    await Promise.all(cart.lines.map((line) => api.removeCartLine(client, line.id, mutationOptions())));

    await expect(api.quoteCart(client, { deliveryMethod: 'SelfPickup' }, mutationOptions())).rejects.toMatchObject({
      code: 'checkout.cart-empty',
    });
  });
});
