/** 驗收條件：網路中斷要顯示 NetworkError 的訊息，不是白畫面。 */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { ApiClient, ApiError } from '@greygray/api-client';
import * as api from '@greygray/api-client/endpoints/storefront';
import { STOREFRONT_BASE_URL, resetStorefrontMockState } from '@greygray/api-client/mock/handlers.storefront';
import { storefrontServer } from '@greygray/api-client/mock/server';
import { problem } from '@greygray/api-client/mock/problems';
import { describeError } from '../errorDisplay';

const client = new ApiClient({ baseUrl: STOREFRONT_BASE_URL });

beforeAll(() => storefrontServer.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  storefrontServer.resetHandlers();
  resetStorefrontMockState();
});
afterAll(() => storefrontServer.close());

describe('describeError', () => {
  it('ApiError：顯示 problem.title 與 traceId 後 8 碼', () => {
    const error = new ApiError(
      problem(422, 'checkout.cart-empty', '購物車是空的', { traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01' }),
    );
    const display = describeError(error);
    expect(display.title).toBe('購物車是空的');
    expect(display.traceId).toBe('0e0e4736-00f067aa0ba902b7-01'.slice(-8));
  });

  it('驗收條件：網路中斷時（fetch 直接失敗）要顯示 NetworkError 的訊息，不是白畫面', async () => {
    storefrontServer.use(
      http.post(`${STOREFRONT_BASE_URL}/v1/cart/quote`, () => HttpResponse.error()),
    );

    expect.assertions(2);
    try {
      await api.quoteCart(client, { deliveryMethod: 'SelfPickup' }, { idempotencyKey: 'network-error-test' });
    } catch (error) {
      const display = describeError(error);
      expect(display.title).toBe('連線失敗，請檢查網路後再試一次。');
      expect(display.traceId).toBeNull();
    }
  });

  it('不認得的錯誤型別也要有一句話，不能讓畫面沒有文字可顯示', () => {
    const display = describeError('不是 Error 物件的東西');
    expect(display.title).toBeTruthy();
  });
});
