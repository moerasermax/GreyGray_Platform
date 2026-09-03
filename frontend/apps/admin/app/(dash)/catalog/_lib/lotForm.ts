/**
 * 進貨表單的**純**轉換與驗證：欄位字串 → `POST /v1/lots` 的 body。
 * 拆出來的理由同 `skuForm.ts`。
 *
 * 請求型別從 `endpoints/admin.ts` 的 `CreateLotRequest` 拿，那一支又是從
 * `paths['/v1/lots']['post']` 推導的——**這裡不留任何手寫的契約複本**。
 */
import { moneyFromMajorInput } from '@greygray/api-client';
import type { CreateLotRequest } from '@greygray/api-client/endpoints/admin';
import type { BuildResult } from './skuForm';

export interface LotFormFields {
  readonly quantity: string;
  readonly unitCostMajor: string;
  readonly batchCode: string;
}

export const EMPTY_LOT_FORM: LotFormFields = { quantity: '', unitCostMajor: '', batchCode: '' };

export function buildLotInput(skuId: string, fields: LotFormFields): BuildResult<CreateLotRequest> {
  const quantity = Number.parseInt(fields.quantity.trim(), 10);
  // 契約寫 `type: integer, minimum: 1`。`Number.parseInt('3.7')` 會回 3，
  // 所以先擋掉「看起來不是整數」的輸入，不要靜靜地把 3.7 件收成 3 件。
  if (!/^\d+$/.test(fields.quantity.trim()) || !Number.isSafeInteger(quantity) || quantity < 1) {
    return { ok: false, message: '進貨數量必須是大於 0 的整數。' };
  }

  const unitCost = fields.unitCostMajor.trim() ? moneyFromMajorInput(fields.unitCostMajor, 'TWD') : null;
  if (!unitCost) {
    return { ok: false, message: '單位成本為必填，最多只能有兩位小數。' };
  }
  if (unitCost.amountMinor < 0) {
    return { ok: false, message: '單位成本不能是負數。' };
  }

  return {
    ok: true,
    body: {
      skuId,
      quantity,
      unitCost,
      batchCode: fields.batchCode.trim() ? fields.batchCode.trim() : null,
    },
  };
}
