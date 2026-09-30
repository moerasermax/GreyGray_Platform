import { describe, expect, it } from 'vitest';
import { PRODUCT_DESCRIPTION_DISPLAY_NOTE, productDescriptionHint } from '../_lib/productDescription';

describe('完整描述提示', () => {
  it('T7：空字串是 0 字', () => {
    expect(productDescriptionHint('')).toBe(`目前 0 字・${PRODUCT_DESCRIPTION_DISPLAY_NOTE}`);
  });

  it('T7：前後空白不算', () => {
    expect(productDescriptionHint('  商品描述  ')).toBe(`目前 4 字・${PRODUCT_DESCRIPTION_DISPLAY_NOTE}`);
  });

  it('T7：內容中的換行算一個字元', () => {
    expect(productDescriptionHint('甲\n乙')).toBe(`目前 3 字・${PRODUCT_DESCRIPTION_DISPLAY_NOTE}`);
  });
});
