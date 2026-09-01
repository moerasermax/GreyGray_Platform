/**
 * 購物車徽章的數字。
 *
 * ── 這裡守的是哪一種病 ──
 * 「畫面宣稱了不成立的事」。#29 是後台首頁把寫死的假數字當成帳務現況，
 * FE-21 是一顆在請求還沒送出時就跳「成功」的按鈕。
 * 一顆在 API 掛掉時仍然理直氣壯顯示 0 的徽章是同一個家族——
 * **「0 件」與「不知道幾件」是兩件事**，而畫面上它們長得一樣。
 * 所以型別把它們分開（`number | null`），這裡逐條釘住兩者不會互換。
 */
import { describe, expect, it } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import { cartTabAccessibleName, countCartItems, formatCartBadge } from '../cartBadge';

type S = components['schemas'];

const ZERO = { amountMinor: 0, currency: 'TWD' } as const;

function line(id: string, quantity: number): S['CartLine'] {
  return {
    id,
    skuId: `sku_${id}`,
    productId: `prd_${id}`,
    name: `商品 ${id}`,
    mode: 'Stock',
    quantity,
    unitPrice: ZERO,
    lineTotal: ZERO,
  };
}

function cart(lines: S['CartLine'][]): S['Cart'] {
  return { id: 'cart_1', lines, goodsTotal: ZERO, hasMixedModes: false };
}

describe('countCartItems', () => {
  it('數的是各行 quantity 的總和，不是 lines.length', () => {
    expect(countCartItems(cart([line('a', 1), line('b', 1), line('c', 1)]))).toBe(3);
    expect(countCartItems(cart([line('a', 2), line('b', 4)]))).toBe(6);
  });

  it('空購物車是 0，不是 null——「真的空」是一個知道的事實', () => {
    expect(countCartItems(cart([]))).toBe(0);
  });

  it.each([
    [null, 'null'],
    [undefined, 'undefined'],
  ])('拿不到購物車（%s）回 null＝不知道，**不是 0**', (input, _label) => {
    expect(countCartItems(input)).toBeNull();
  });

  it('形狀不對（沒有 lines）也回 null，不會炸掉也不會謊報 0', () => {
    expect(countCartItems({ id: 'x' } as unknown as S['Cart'])).toBeNull();
  });
});

describe('★ 裁決：徽章數的是「件數」不是「品項數」——這一段擋住改回 lines.length', () => {
  /*
   * 為什麼需要專門一條：`lines.length` 與數量總和在「每行都買 1 個」時**完全一樣**，
   * 上面那組 1+1+1=3 兩種取法都會綠。只有一行、quantity 不是 1 的購物車才分得出來。
   *
   * 而這個情境不是硬湊的——對 dev 後端實測，**後端會把同一個 SKU 的重複加入併進同一行**
   * （加 A×2 再加 A×3，回來是一行、quantity=5），所以「一行、quantity=5」
   * 正是使用者連加兩次之後購物車真正的樣子。
   */
  const oneLineOfFive = cart([line('a', 5)]);

  it('一行、quantity=5 → 數到 5（若有人改回 lines.length，這裡會變成 1）', () => {
    expect(countCartItems(oneLineOfFive)).toBe(5);
  });

  it('可及名稱唸「購物車，5 件」——唸成「1 件」是錯的，使用者手上有 5 件', () => {
    expect(cartTabAccessibleName(countCartItems(oneLineOfFive))).toBe('購物車，5 件');
  });

  it('徽章印 5', () => {
    expect(formatCartBadge(countCartItems(oneLineOfFive))).toBe('5');
  });

  it('同一個 SKU 再加一次，徽章會動——這是「加入成功後徽章必須立刻更新」的前提', () => {
    const before = countCartItems(cart([line('a', 2)]));
    const after = countCartItems(cart([line('a', 5)])); // 後端把 +3 併進同一行
    expect(before).toBe(2);
    expect(after).toBe(5);
    expect(after).not.toBe(before);
  });
});

describe('壞掉的 quantity 一律回 null，絕對不能變成 NaN', () => {
  /*
   * 徽章上出現 `NaN` 比顯示 0 還糟。用 `reduce` 直接加的話：
   * `undefined` → NaN、字串 → 字串串接，兩種都會被畫到畫面上。
   * 判準是「有一行不可信，整個數字就不可信」——不跳過壞掉那一行，
   * 因為跳過會安靜地少算，那又是一個不成立的數字。
   */
  function brokenLine(quantity: unknown): S['CartLine'] {
    return { ...line('bad', 1), quantity } as unknown as S['CartLine'];
  }

  it.each([
    ['undefined', undefined],
    ['null', null],
    ['字串', '3'],
    ['NaN', Number.NaN],
    ['Infinity', Number.POSITIVE_INFINITY],
    ['負數', -1],
  ])('quantity 是 %s → 回 null', (_label, quantity) => {
    expect(countCartItems(cart([brokenLine(quantity)]))).toBeNull();
  });

  it('壞掉的那一行混在好的裡面也一樣回 null，不是把好的加一加就交差', () => {
    expect(countCartItems(cart([line('a', 2), brokenLine(undefined), line('c', 3)]))).toBeNull();
  });

  it('回傳值永遠不是 NaN——這是這一段真正要擋的東西', () => {
    const result = countCartItems(cart([brokenLine(undefined)]));
    expect(result).not.toBeNaN();
    expect(formatCartBadge(result)).toBeNull();
    expect(cartTabAccessibleName(result)).toBe('購物車');
  });
});

describe('formatCartBadge', () => {
  it('不知道幾件時不畫徽章——這一條是這一包的紅線', () => {
    expect(formatCartBadge(null)).toBeNull();
  });

  it('真的空車也不畫徽章：徽章的作用是提醒你有東西，一個 0 只是雜訊', () => {
    expect(formatCartBadge(0)).toBeNull();
  });

  it.each([
    [1, '1'],
    [9, '9'],
    [99, '99'],
  ])('%d 件印 %s', (count, expected) => {
    expect(formatCartBadge(count)).toBe(expected);
  });

  it('超過 99 收成 99+，否則徽章會把分頁擠歪', () => {
    expect(formatCartBadge(100)).toBe('99+');
    expect(formatCartBadge(1234)).toBe('99+');
  });

  it('不該出現的值（負數、NaN）也不畫，而不是印出來嚇人', () => {
    expect(formatCartBadge(-1)).toBeNull();
    expect(formatCartBadge(Number.NaN)).toBeNull();
  });
});

describe('cartTabAccessibleName：畫面上分不出來的，唸出來要分得出來', () => {
  it('不知道幾件時只說「購物車」，**不會被講成 0 件**', () => {
    expect(cartTabAccessibleName(null)).toBe('購物車');
  });

  it('真的空車會說「目前是空的」——這才是空車與不知道真正分家的地方', () => {
    expect(cartTabAccessibleName(0)).toBe('購物車，目前是空的');
  });

  it('有東西時把件數唸出來', () => {
    expect(cartTabAccessibleName(3)).toBe('購物車，3 件');
  });

  it('超過 99 唸真實數字，不跟著徽章收成 99+——螢幕閱讀器沒有版面限制', () => {
    expect(cartTabAccessibleName(120)).toBe('購物車，120 件');
  });

  it('「不知道」與「空車」唸出來一定不一樣', () => {
    expect(cartTabAccessibleName(null)).not.toBe(cartTabAccessibleName(0));
  });
});
