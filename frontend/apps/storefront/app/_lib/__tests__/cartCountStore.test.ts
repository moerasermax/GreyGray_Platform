/**
 * 「加入購物車之後，徽章不重新整理就要變」——這件事能不能成立，全看這個 store。
 *
 * ── 為什麼測得到 ──
 * store 是純 JavaScript（一個模組層級的值 ＋ 一組 listener），沒有 React、沒有 DOM。
 * 分頁列那邊只是 `useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)`
 * 的三行接線。把狀態抽出來，就不需要 jsdom 也測得到「推一份新的 Cart 進來，
 * 訂閱者會被通知，而且讀到的是新數字」。
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '@greygray/api-client/storefront';
import {
  getCartItemCount,
  getServerCartItemCount,
  publishCart,
  resetCartItemCountStore,
  setCartItemCount,
  subscribeCartItemCount,
} from '../cartCountStore';

type S = components['schemas'];

const ZERO = { amountMinor: 0, currency: 'TWD' } as const;

function cartWith(lineCount: number): S['Cart'] {
  return {
    id: 'cart_1',
    lines: Array.from({ length: lineCount }, (_, index) => ({
      id: `line_${index}`,
      skuId: `sku_${index}`,
      productId: `prd_${index}`,
      name: `商品 ${index}`,
      mode: 'Stock' as const,
      quantity: 1,
      unitPrice: ZERO,
      lineTotal: ZERO,
    })),
    goodsTotal: ZERO,
    hasMixedModes: false,
  };
}

beforeEach(() => {
  resetCartItemCountStore();
});

describe('起始狀態', () => {
  it('一開始是 null＝還沒問過，不是 0', () => {
    expect(getCartItemCount()).toBeNull();
  });

  it('SSR 快照永遠是 null——伺服器不知道這個瀏覽器的購物車，猜一個就是說謊', () => {
    expect(getServerCartItemCount()).toBeNull();
    setCartItemCount(7);
    expect(getServerCartItemCount()).toBeNull();
  });
});

describe('publishCart：加入購物車之後徽章立刻更新', () => {
  it('推一份新的 Cart 進來，訂閱者被通知，而且讀到新數字', () => {
    const listener = vi.fn();
    subscribeCartItemCount(listener);

    publishCart(cartWith(2));

    expect(listener).toHaveBeenCalledTimes(1);
    expect(getCartItemCount()).toBe(2);
  });

  it('連續加入會一路跟上（1 → 2 → 3），不必重新整理', () => {
    const seen: (number | null)[] = [];
    subscribeCartItemCount(() => seen.push(getCartItemCount()));

    publishCart(cartWith(1));
    publishCart(cartWith(2));
    publishCart(cartWith(3));

    expect(seen).toEqual([1, 2, 3]);
  });

  it('在購物車頁移除到剩 1 行，徽章跟著降下來', () => {
    publishCart(cartWith(3));
    publishCart(cartWith(1));

    expect(getCartItemCount()).toBe(1);
  });

  it('推 null（拿不到購物車）會回到「不知道」，而不是掉成 0', () => {
    publishCart(cartWith(2));
    publishCart(null);

    expect(getCartItemCount()).toBeNull();
  });
});

describe('通知語意', () => {
  it('值沒變就不通知——同一個數字重複推不該引發 re-render', () => {
    publishCart(cartWith(2));
    const listener = vi.fn();
    subscribeCartItemCount(listener);

    publishCart(cartWith(2));

    expect(listener).not.toHaveBeenCalled();
  });

  it('退訂之後不再被通知', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeCartItemCount(listener);

    unsubscribe();
    publishCart(cartWith(1));

    expect(listener).not.toHaveBeenCalled();
  });

  it('listener 在被通知的當下退訂，不會弄壞這一輪的其他 listener', () => {
    const second = vi.fn();
    const unsubscribeFirst = subscribeCartItemCount(() => unsubscribeFirst());
    subscribeCartItemCount(second);

    expect(() => publishCart(cartWith(1))).not.toThrow();
    expect(second).toHaveBeenCalledTimes(1);
  });

  it('多個訂閱者都收得到（分頁列可能因為路由切換而重新掛載）', () => {
    const a = vi.fn();
    const b = vi.fn();
    subscribeCartItemCount(a);
    subscribeCartItemCount(b);

    publishCart(cartWith(5));

    expect(a).toHaveBeenCalledTimes(1);
    expect(b).toHaveBeenCalledTimes(1);
  });
});
