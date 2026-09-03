/**
 * 小計預覽（ADR-033 的唯一前端乘法例外）。
 *
 * 這支測試存在的理由跟函式本身一樣：例外一旦沒有測試，它就會變成慣例。
 * 非法輸入要 throw 而不是回 0——回 0 會在畫面上長成一個看起來很正常的 NT$0。
 */
import { describe, expect, it } from 'vitest';
import type { Money } from '@greygray/api-client';
import { subtotalPreview } from '../subtotalPreview';

const NT60: Money = { amountMinor: 6000, currency: 'TWD' };

describe('subtotalPreview：單價 × 數量', () => {
  it('NT$60 × 5 = NT$300（使用者 #40 回報的那一組數字）', () => {
    expect(subtotalPreview(NT60, 5)).toEqual({ amountMinor: 30000, currency: 'TWD' });
  });

  it('數量 1 就是單價本身', () => {
    expect(subtotalPreview(NT60, 1)).toEqual({ amountMinor: 6000, currency: 'TWD' });
  });

  it('幣別原樣帶出，不換算（JPY 沒有小數位，換算就是 bug）', () => {
    expect(subtotalPreview({ amountMinor: 1000, currency: 'JPY' }, 3)).toEqual({
      amountMinor: 3000,
      currency: 'JPY',
    });
  });

  it('0 元 × N 還是 0 元（免費贈品是合法價格，不是「沒有價格」）', () => {
    expect(subtotalPreview({ amountMinor: 0, currency: 'TWD' }, 7)).toEqual({
      amountMinor: 0,
      currency: 'TWD',
    });
  });

  it('不會就地改動傳進來的 Money', () => {
    const price: Money = { amountMinor: 6000, currency: 'TWD' };
    subtotalPreview(price, 5);
    expect(price).toEqual({ amountMinor: 6000, currency: 'TWD' });
  });

  it.each([
    [0, '數量 0'],
    [-1, '負數量'],
    [1.5, '非整數數量'],
    [Number.NaN, 'NaN'],
    [Number.POSITIVE_INFINITY, 'Infinity'],
  ])('數量 %o 直接 throw（%s）', (quantity) => {
    expect(() => subtotalPreview(NT60, quantity)).toThrow(RangeError);
  });

  it.each([
    [-1, '負金額'],
    [10.5, '非整數最小單位——最小單位本來就該是整數'],
    [Number.NaN, 'NaN'],
  ])('金額 %o 直接 throw（%s）', (amountMinor) => {
    expect(() => subtotalPreview({ amountMinor, currency: 'TWD' }, 2)).toThrow(RangeError);
  });

  it('相乘超出安全整數範圍時 throw，不回一個已經失真的數字', () => {
    expect(() => subtotalPreview({ amountMinor: Number.MAX_SAFE_INTEGER, currency: 'TWD' }, 2)).toThrow(
      RangeError,
    );
  });
});
