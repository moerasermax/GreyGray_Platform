/**
 * #36：登出之後購物車徽章要立刻不見，而登出**失敗**時不可以動它。
 *
 * 用的是真的 `cartCountStore`（不是 mock）——這條規則的價值就在於
 * 「畫面上那個數字」真的變了，換成假的 store 只會測到自己寫的假貨。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  getCartItemCount,
  publishCart,
  resetCartItemCountStore,
} from '../../../_lib/cartCountStore';
import { performLogout } from '../logout';

/** 一台有 3 件東西的購物車，形狀只取 `countCartItems` 讀得到的部分。 */
const CART_WITH_3 = {
  id: 'cart_1',
  lines: [{ quantity: 2 }, { quantity: 1 }],
} as unknown as Parameters<typeof publishCart>[0];

beforeEach(() => {
  resetCartItemCountStore();
});

describe('登出成功', () => {
  it('徽章歸零（＝不知道幾件、不畫徽章），而且是在導向之前', async () => {
    publishCart(CART_WITH_3);
    expect(getCartItemCount()).toBe(3);

    const seenAtGoHome: unknown[] = [];
    const ok = await performLogout({
      logout: async () => undefined,
      goHome: () => seenAtGoHome.push(getCartItemCount()),
    });

    expect(ok).toBe(true);
    expect(getCartItemCount()).toBeNull();
    // 導向發生時徽章**已經**歸零——反過來的話首頁會閃一下舊數字。
    expect(seenAtGoHome).toEqual([null]);
  });
});

describe('登出失敗', () => {
  it('徽章維持原值——cookie 還在，清成空的是說謊', async () => {
    publishCart(CART_WITH_3);

    const goHome = vi.fn();
    const ok = await performLogout({
      logout: async () => {
        throw new Error('500');
      },
      goHome,
    });

    expect(ok).toBe(false);
    expect(getCartItemCount()).toBe(3);
    expect(goHome).not.toHaveBeenCalled();
  });
});
