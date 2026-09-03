/**
 * SKU 表單的規則（`_lib/skuForm.ts`）。新增與編輯共用同一份，所以這裡驗一次就夠。
 *
 * 這個 workspace 沒有 jsdom／testing-library（見 `(dash)/__tests__/dashboardLedger.test.tsx`
 * 檔頭），所以「填了什麼會送出什麼」是拆成純函式驗的，不是靠敲鍵盤。
 */
import { describe, expect, it } from 'vitest';
import { EMPTY_SKU_FORM, buildSkuInput, skuFormFromSku, type SkuFormFields } from '../_lib/skuForm';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

const FILLED: SkuFormFields = {
  name: '  森田面膜  ',
  variantName: ' 30 入 ',
  weightGram: '120',
  lengthCm: '8',
  widthCm: '6',
  heightCm: '4',
  unitOfMeasure: ' 盒 ',
  unitCount: '30',
  listPriceMajor: '180',
  isActive: true,
};

describe('buildSkuInput：欄位字串 → AdminSkuInput', () => {
  it('新增模式的起點是空表單，但 isActive 預設 true', () => {
    expect(EMPTY_SKU_FORM.name).toBe('');
    expect(EMPTY_SKU_FORM.listPriceMajor).toBe('');
    expect(EMPTY_SKU_FORM.isActive).toBe(true);
  });

  it('填齊後送出的 body 逐欄位等於契約形狀，NT$180 是 18000 minor', () => {
    const built = buildSkuInput(FILLED);

    expect(built.ok).toBe(true);
    expect(built.ok && built.body).toEqual({
      name: '森田面膜',
      variantName: '30 入',
      weightGram: 120,
      size: { lengthCm: 8, widthCm: 6, heightCm: 4 },
      unitOfMeasure: '盒',
      unitCount: 30,
      listPrice: { amountMinor: 18000, currency: 'TWD' },
      isActive: true,
    });
  });

  it('選填欄位留白送 null，不是空字串——契約要的是 null', () => {
    const built = buildSkuInput({ ...FILLED, variantName: '  ', unitOfMeasure: '', unitCount: '', listPriceMajor: '' });

    expect(built.ok && built.body.variantName).toBeNull();
    expect(built.ok && built.body.unitOfMeasure).toBeNull();
    expect(built.ok && built.body.unitCount).toBeNull();
    expect(built.ok && built.body.listPrice).toBeNull();
  });

  it('★ 重量與尺寸缺一就擋下來，不准在前端捏造預設值（ADR-032）', () => {
    for (const missing of [{ weightGram: '' }, { lengthCm: '' }, { widthCm: '' }, { heightCm: '' }, { name: '  ' }]) {
      const built = buildSkuInput({ ...FILLED, ...missing });
      expect(built.ok, JSON.stringify(missing)).toBe(false);
      expect(built.ok === false && built.message).toContain('必填');
    }
  });

  it('標價超過兩位小數是錯誤，不是無聲四捨五入', () => {
    const built = buildSkuInput({ ...FILLED, listPriceMajor: '180.123' });

    expect(built.ok).toBe(false);
    expect(built.ok === false && built.message).toContain('兩位小數');
  });

  it('skuFormFromSku → buildSkuInput 來回一趟不會改動任何值', () => {
    const sku: S['AdminSku'] = {
      id: 'aaaaaaaabbbbbbbbccccccccdddddddd',
      available: 12,
      name: '若元錠 EX',
      variantName: null,
      weightGram: 400,
      size: { lengthCm: 8, widthCm: 6, heightCm: 4 },
      unitOfMeasure: null,
      unitCount: null,
      listPrice: { amountMinor: 89000, currency: 'TWD' },
      isActive: false,
    };

    const built = buildSkuInput(skuFormFromSku(sku));

    expect(built.ok && built.body).toEqual({
      name: sku.name,
      variantName: null,
      weightGram: 400,
      size: sku.size,
      unitOfMeasure: null,
      unitCount: null,
      listPrice: { amountMinor: 89000, currency: 'TWD' },
      isActive: false,
    });
  });
});
