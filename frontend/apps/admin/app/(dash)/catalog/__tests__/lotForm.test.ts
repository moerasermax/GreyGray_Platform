/** 進貨表單的規則（`_lib/lotForm.ts`）。理由同 `skuForm.test.ts`。 */
import { describe, expect, it } from 'vitest';
import { EMPTY_LOT_FORM, buildLotInput } from '../_lib/lotForm';

const SKU_ID = 'aaaaaaaabbbbbbbbccccccccdddddddd';

describe('buildLotInput：欄位字串 → POST /v1/lots 的 body', () => {
  it('填齊後送出的 body 逐欄位等於契約形狀，NT$120 是 12000 minor', () => {
    const built = buildLotInput(SKU_ID, { quantity: '10', unitCostMajor: '120', batchCode: ' B-2026-09 ' });

    expect(built.ok && built.body).toEqual({
      skuId: SKU_ID,
      quantity: 10,
      unitCost: { amountMinor: 12000, currency: 'TWD' },
      batchCode: 'B-2026-09',
    });
  });

  it('批號留白送 null', () => {
    const built = buildLotInput(SKU_ID, { ...EMPTY_LOT_FORM, quantity: '1', unitCostMajor: '1' });

    expect(built.ok && built.body.batchCode).toBeNull();
  });

  it('★ 數量必須是 ≥ 1 的整數：0、負數、小數、空白都擋下來', () => {
    for (const quantity of ['0', '-3', '3.7', '', '  ', 'abc']) {
      const built = buildLotInput(SKU_ID, { quantity, unitCostMajor: '120', batchCode: '' });
      expect(built.ok, `quantity=${quantity}`).toBe(false);
      expect(built.ok === false && built.message).toContain('整數');
    }
  });

  it('「3.7」不會被無聲收成 3 件——parseInt 會，這裡不會', () => {
    const built = buildLotInput(SKU_ID, { quantity: '3.7', unitCostMajor: '120', batchCode: '' });

    expect(built.ok).toBe(false);
  });

  it('單位成本是必填，格式錯也擋', () => {
    expect(buildLotInput(SKU_ID, { quantity: '1', unitCostMajor: '', batchCode: '' }).ok).toBe(false);
    expect(buildLotInput(SKU_ID, { quantity: '1', unitCostMajor: '12.345', batchCode: '' }).ok).toBe(false);
  });

  it('單位成本不能是負數', () => {
    const built = buildLotInput(SKU_ID, { quantity: '1', unitCostMajor: '-1', batchCode: '' });

    expect(built.ok).toBe(false);
    expect(built.ok === false && built.message).toContain('負數');
  });
});
