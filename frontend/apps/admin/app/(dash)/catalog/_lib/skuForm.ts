/**
 * SKU 表單的**純**轉換與驗證：欄位字串 → `AdminSkuInput`。
 *
 * 為什麼要從 Drawer 裡拆出來：這個 workspace 沒有 jsdom／testing-library
 * （見 `(dash)/__tests__/dashboardLedger.test.tsx` 檔頭），effect 與事件跑不起來，
 * 「填了什麼會送出什麼」只有拆成純函式才驗得到。**新增與編輯共用同一份規則**——
 * 兩套規則遲早會分岔，而分岔的那一天沒有任何東西會說話。
 */
import { moneyFromMajorInput, moneyToMajorInput } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';

type S = components['schemas'];

export interface SkuFormFields {
  readonly name: string;
  readonly variantName: string;
  readonly weightGram: string;
  readonly lengthCm: string;
  readonly widthCm: string;
  readonly heightCm: string;
  readonly unitOfMeasure: string;
  readonly unitCount: string;
  readonly listPriceMajor: string;
  readonly isActive: boolean;
}

/** 新增模式的起點。`isActive` 預設 true——剛建好的 SKU 就是要能賣。 */
export const EMPTY_SKU_FORM: SkuFormFields = {
  name: '',
  variantName: '',
  weightGram: '',
  lengthCm: '',
  widthCm: '',
  heightCm: '',
  unitOfMeasure: '',
  unitCount: '',
  listPriceMajor: '',
  isActive: true,
};

export function skuFormFromSku(sku: S['AdminSku']): SkuFormFields {
  return {
    name: sku.name,
    variantName: sku.variantName ?? '',
    weightGram: String(sku.weightGram),
    lengthCm: String(sku.size.lengthCm),
    widthCm: String(sku.size.widthCm),
    heightCm: String(sku.size.heightCm),
    unitOfMeasure: sku.unitOfMeasure ?? '',
    unitCount: sku.unitCount !== null && sku.unitCount !== undefined ? String(sku.unitCount) : '',
    listPriceMajor: sku.listPrice ? moneyToMajorInput(sku.listPrice) : '',
    isActive: sku.isActive,
  };
}

export type BuildResult<T> =
  | { readonly ok: true; readonly body: T }
  | { readonly ok: false; readonly message: string };

/**
 * `weightGram` 與 `size` 是必填（契約 `AdminSkuInput.required`，ADR-032 再次確認）——
 * **不准在前端捏造預設值**，缺了就擋在這裡，讓人回去填。
 */
export function buildSkuInput(fields: SkuFormFields): BuildResult<S['AdminSkuInput']> {
  const weight = Number.parseInt(fields.weightGram, 10);
  const length = Number.parseFloat(fields.lengthCm);
  const width = Number.parseFloat(fields.widthCm);
  const height = Number.parseFloat(fields.heightCm);
  if (
    !fields.name.trim() ||
    !Number.isFinite(weight) ||
    !Number.isFinite(length) ||
    !Number.isFinite(width) ||
    !Number.isFinite(height)
  ) {
    return { ok: false, message: '名稱、重量與尺寸為必填，且必須是數字。' };
  }

  const listPrice = fields.listPriceMajor.trim() ? moneyFromMajorInput(fields.listPriceMajor, 'TWD') : null;
  if (fields.listPriceMajor.trim() && !listPrice) {
    return { ok: false, message: '現貨標價格式不正確，最多只能有兩位小數。' };
  }

  return {
    ok: true,
    body: {
      name: fields.name.trim(),
      variantName: fields.variantName.trim() ? fields.variantName.trim() : null,
      weightGram: weight,
      size: { lengthCm: length, widthCm: width, heightCm: height },
      unitOfMeasure: fields.unitOfMeasure.trim() ? fields.unitOfMeasure.trim() : null,
      unitCount: fields.unitCount.trim() ? Number.parseInt(fields.unitCount, 10) : null,
      listPrice,
      isActive: fields.isActive,
    },
  };
}
