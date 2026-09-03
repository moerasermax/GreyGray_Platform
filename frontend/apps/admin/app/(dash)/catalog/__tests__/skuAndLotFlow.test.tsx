/**
 * 新增 SKU 與進貨兩條流程，打到 api-client 的 admin mock（真的 `ApiClient` ＋ msw）。
 *
 * 這裡刻意**不**重貼一份請求 JSON：body 一律由 `_lib/skuForm.ts`／`_lib/lotForm.ts`
 * 從「使用者填的字串」建出來，再交給 `endpoints/admin.ts` 送出。抄一份 body 進測試
 * 只會驗到「我抄對了嗎」，驗不到畫面填的東西會變成什麼。
 */
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import { setupServer } from 'msw/node';
import { ApiClient, ApiError } from '@greygray/api-client';
import { createLot, createSku, getProduct, listLots, listProducts } from '@greygray/api-client/endpoints/admin';
import { ADMIN_BASE_URL, adminErrorScenarios, adminHandlers, resetAdminMockState } from '@greygray/api-client/mock/handlers.admin';
import { LotDrawerBody } from '../_components/LotDrawer';
import { EMPTY_LOT_FORM, buildLotInput } from '../_lib/lotForm';
import { EMPTY_SKU_FORM, buildSkuInput } from '../_lib/skuForm';
import { apiFieldErrors } from '../_lib/apiError';

(globalThis as unknown as { React: typeof React }).React = React;

const server = setupServer(...adminHandlers);
const client = new ApiClient({ baseUrl: ADMIN_BASE_URL });
let sequence = 0;
const mutation = () => ({ idempotencyKey: `fe27-${++sequence}` });

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  server.resetHandlers(...adminHandlers);
  resetAdminMockState();
});
afterAll(() => server.close());

async function firstProduct() {
  const page = await listProducts(client);
  const product = page.items[0];
  expect(product).toBeDefined();
  return product!;
}

describe('新增 SKU（POST /v1/products/{productId}/skus，ADR-032）', () => {
  it('★ 表單填的字串 → 建立 → 商品重新 GET 之後就看得到那一筆', async () => {
    const product = await firstProduct();
    const before = product.skus.length;

    const built = buildSkuInput({
      ...EMPTY_SKU_FORM,
      name: '玻尿酸面膜 補貨',
      variantName: '10 入',
      weightGram: '120',
      lengthCm: '8',
      widthCm: '6',
      heightCm: '4',
      listPriceMajor: '180',
    });
    expect(built.ok).toBe(true);

    const created = await createSku(client, product.id, built.ok ? built.body : ({} as never), mutation());

    expect(created.name).toBe('玻尿酸面膜 補貨');
    expect(created.listPrice).toEqual({ amountMinor: 18000, currency: 'TWD' });
    // 新 SKU 的可用量是後端算的，一定是 0——前端沒有猜。
    expect(created.available).toBe(0);

    const reloaded = await getProduct(client, product.id);
    expect(reloaded.skus).toHaveLength(before + 1);
    expect(reloaded.skus.at(-1)?.id).toBe(created.id);
  });

  it('★ 422 帶得回逐欄位錯誤，看得出是哪一欄', async () => {
    server.use(adminErrorScenarios.skuValidation);
    const product = await firstProduct();
    const built = buildSkuInput({ ...EMPTY_SKU_FORM, name: 'x', weightGram: '0', lengthCm: '0', widthCm: '0', heightCm: '0' });

    const cause = await createSku(client, product.id, built.ok ? built.body : ({} as never), mutation()).catch(
      (error: unknown) => error,
    );

    expect(cause).toBeInstanceOf(ApiError);
    expect((cause as ApiError).problem.status).toBe(422);
    expect(apiFieldErrors(cause)).toEqual({ weightGram: ['重量為必填。'], size: ['尺寸為必填。'] });
  });
});

describe('進貨（POST /v1/lots ＋ GET /v1/lots，M2）', () => {
  it('★ 建立批號後，批號列表看得到，SKU 的可用量跟著變', async () => {
    const product = await firstProduct();
    const sku = product.skus[0]!;
    const availableBefore = sku.available;

    expect((await listLots(client, { skuId: sku.id })).items).toEqual([]);

    const built = buildLotInput(sku.id, { ...EMPTY_LOT_FORM, quantity: '10', unitCostMajor: '120', batchCode: 'B-2026-09' });
    expect(built.ok).toBe(true);
    const lot = await createLot(client, built.ok ? built.body : ({} as never), mutation());

    expect(lot.unitCost).toEqual({ amountMinor: 12000, currency: 'TWD' });
    expect(lot.quantityAvailable).toBe(10);

    const lots = await listLots(client, { skuId: sku.id });
    expect(lots.items.map((item) => item.id)).toEqual([lot.id]);

    // 可用量由後端回，前端沒有自己加——但它確實變了。
    const reloaded = await getProduct(client, product.id);
    expect(reloaded.skus.find((item) => item.id === sku.id)?.available).toBe(availableBefore + 10);
  });

  it('批號只列這個 SKU 的，不是全部', async () => {
    const product = await firstProduct();
    const [first] = product.skus;
    const other = (await listProducts(client)).items[1]!.skus[0]!;

    await createLot(client, { skuId: first!.id, quantity: 3, unitCost: { amountMinor: 100, currency: 'TWD' } }, mutation());
    await createLot(client, { skuId: other.id, quantity: 5, unitCost: { amountMinor: 200, currency: 'TWD' } }, mutation());

    const lots = await listLots(client, { skuId: other.id });
    expect(lots.items.map((item) => item.quantityOnHand)).toEqual([5]);
  });

  it('抽屜畫出來的批號逐字等於端點回的那一筆', async () => {
    const product = await firstProduct();
    const sku = product.skus[0]!;
    await createLot(
      client,
      { skuId: sku.id, quantity: 10, unitCost: { amountMinor: 12000, currency: 'TWD' }, batchCode: 'B-2026-09' },
      mutation(),
    );
    const lots = await listLots(client, { skuId: sku.id });

    const html = renderToStaticMarkup(
      <LotDrawerBody
        sku={sku}
        lots={lots.items}
        loading={false}
        listError={null}
        fields={EMPTY_LOT_FORM}
        error={null}
        fieldErrors={{}}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('B-2026-09');
    expect(html).toContain('NT$120');
    expect(html).toContain(`目前可用量 ${sku.available}`);
  });

  it('還沒進過貨時，抽屜說清楚「可用量是 0」的原因，而不是空白', () => {
    const html = renderToStaticMarkup(
      <LotDrawerBody
        sku={null}
        lots={[]}
        loading={false}
        listError={null}
        fields={EMPTY_LOT_FORM}
        error={null}
        fieldErrors={{}}
        onChange={() => {}}
      />,
    );

    expect(html).toContain('還沒有任何批號');
    expect(html).toContain('這個 SKU 還沒進過貨');
  });
});
