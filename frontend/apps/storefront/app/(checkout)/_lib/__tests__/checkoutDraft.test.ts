/**
 * 結帳草稿：被 401 彈去登入、回來之後已填的內容還在。
 *
 * 這個 workspace 沒有 jsdom，`sessionStorage` 在測試環境裡不存在，
 * 所以每一支公開函式都收一個 `DraftStorage`，這裡餵一個 Map 做的假貨。
 * 那個參數不是為了測試才存在的空殼——它同時是「拿不到 storage 就當作沒有草稿」
 * 那條規則的入口（`null` 是合法的值）。
 */
import { describe, expect, it } from 'vitest';
import {
  CHECKOUT_DRAFT_KEY_PREFIX,
  checkoutDraftKey,
  clearCheckoutDraft,
  clearOtherCheckoutDrafts,
  emptyCheckoutDraft,
  isEmptyDraft,
  loadCheckoutDraft,
  parseCheckoutDraft,
  saveCheckoutDraft,
  serializeCheckoutDraft,
  type CheckoutDraft,
  type DraftStorage,
} from '../checkoutDraft';

function fakeStorage(initial: Record<string, string> = {}): DraftStorage & { map: Map<string, string> } {
  const map = new Map(Object.entries(initial));
  return {
    map,
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => void map.set(key, value),
    removeItem: (key) => void map.delete(key),
    get length() {
      return map.size;
    },
    key: (index) => [...map.keys()][index] ?? null,
  };
}

/** 抛例外的 storage：Safari 私密瀏覽的形狀。 */
function throwingStorage(): DraftStorage {
  return {
    getItem: () => {
      throw new Error('SecurityError');
    },
    setItem: () => {
      throw new Error('QuotaExceededError');
    },
    removeItem: () => {
      throw new Error('SecurityError');
    },
    get length(): number {
      throw new Error('SecurityError');
    },
    key: () => {
      throw new Error('SecurityError');
    },
  };
}

const FILLED: CheckoutDraft = {
  deliveryMethod: 'ConvenienceStore',
  shippingPolicy: 'HoldUntilComplete',
  shippingAddressId: 'addr_1',
  convenienceStoreCode: '991234',
  buyerNote: '麻煩包好一點',
};

describe('存 → 取回，五個欄位一字不差', () => {
  it('存進去再讀出來，跟原本填的一樣', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    expect(loadCheckoutDraft('cart_1', storage)).toEqual(FILLED);
  });

  it('鍵含 cart id——兩張車的草稿不會互相蓋掉', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    saveCheckoutDraft('cart_2', { ...FILLED, buyerNote: '另一張車' }, storage);

    expect(checkoutDraftKey('cart_1')).toBe(`${CHECKOUT_DRAFT_KEY_PREFIX}cart_1`);
    expect(loadCheckoutDraft('cart_1', storage)?.buyerNote).toBe('麻煩包好一點');
    expect(loadCheckoutDraft('cart_2', storage)?.buyerNote).toBe('另一張車');
  });

  it('只存那五個欄位，沒有把整份購物車存進去', () => {
    const raw = serializeCheckoutDraft('cart_1', FILLED);
    expect(Object.keys(JSON.parse(raw) as object).sort()).toEqual(
      [
        'buyerNote',
        'cartId',
        'convenienceStoreCode',
        'deliveryMethod',
        'shippingAddressId',
        'shippingPolicy',
      ].sort(),
    );
  });
});

describe('cart id 不同就不還原', () => {
  it('值裡記的 cartId 跟現在這張車不同時當作沒有草稿', () => {
    const raw = serializeCheckoutDraft('cart_1', FILLED);
    expect(parseCheckoutDraft(raw, 'cart_2')).toBeNull();
  });

  it('換了一張車，讀不到上一張的草稿', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    expect(loadCheckoutDraft('cart_2', storage)).toBeNull();
  });

  it('clearOtherCheckoutDrafts 掃掉別張車的殘留，留下自己那筆', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    saveCheckoutDraft('cart_2', FILLED, storage);
    saveCheckoutDraft('cart_3', FILLED, storage);
    storage.map.set('gg:other', 'x');

    clearOtherCheckoutDrafts('cart_2', storage);

    expect([...storage.map.keys()].sort()).toEqual([checkoutDraftKey('cart_2'), 'gg:other'].sort());
  });
});

describe('送出成功之後要刪掉', () => {
  it('clearCheckoutDraft 之後讀不到', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    clearCheckoutDraft('cart_1', storage);
    expect(loadCheckoutDraft('cart_1', storage)).toBeNull();
    expect(storage.map.has(checkoutDraftKey('cart_1'))).toBe(false);
  });

  it('空草稿等同清掉——沒填過任何東西就不該留一筆垃圾', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', FILLED, storage);
    saveCheckoutDraft('cart_1', emptyCheckoutDraft(), storage);
    expect(storage.map.size).toBe(0);
    expect(isEmptyDraft(emptyCheckoutDraft())).toBe(true);
    expect(isEmptyDraft(FILLED)).toBe(false);
  });
});

describe('壞掉的東西一律當作沒有草稿', () => {
  it.each([
    ['不是 JSON', '{oops'],
    ['是陣列', '[]'],
    ['是字串', '"hello"'],
    ['是 null', 'null'],
    ['沒有 cartId', JSON.stringify({ buyerNote: 'x' })],
    ['空字串', ''],
  ])('%s → null', (_label, raw) => {
    expect(parseCheckoutDraft(raw, 'cart_1')).toBeNull();
  });

  it('沒存過就是 null', () => {
    expect(parseCheckoutDraft(null, 'cart_1')).toBeNull();
    expect(loadCheckoutDraft('cart_1', fakeStorage())).toBeNull();
  });

  it('列舉值不認得就退回 null，不會把垃圾送進 payload', () => {
    const raw = JSON.stringify({
      cartId: 'cart_1',
      deliveryMethod: 'Teleport',
      shippingPolicy: 'Whenever',
      shippingAddressId: 42,
      convenienceStoreCode: { evil: true },
      buyerNote: 'ok',
    });
    expect(parseCheckoutDraft(raw, 'cart_1')).toEqual({
      deliveryMethod: null,
      shippingPolicy: null,
      shippingAddressId: null,
      convenienceStoreCode: '',
      buyerNote: 'ok',
    });
  });
});

describe('拿不到 sessionStorage 時頁面照常', () => {
  it('storage 是 null：存不會炸、讀回 null', () => {
    expect(() => saveCheckoutDraft('cart_1', FILLED, null)).not.toThrow();
    expect(() => clearCheckoutDraft('cart_1', null)).not.toThrow();
    expect(() => clearOtherCheckoutDrafts('cart_1', null)).not.toThrow();
    expect(loadCheckoutDraft('cart_1', null)).toBeNull();
  });

  it('隱私模式（每一支都丟例外）：一樣不炸，讀回 null', () => {
    const storage = throwingStorage();
    expect(() => saveCheckoutDraft('cart_1', FILLED, storage)).not.toThrow();
    expect(() => clearCheckoutDraft('cart_1', storage)).not.toThrow();
    expect(() => clearOtherCheckoutDrafts('cart_1', storage)).not.toThrow();
    expect(loadCheckoutDraft('cart_1', storage)).toBeNull();
  });
});
