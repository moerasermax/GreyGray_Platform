/**
 * 結帳頁被 401 彈去登入、登入完再回來時，把已填的內容帶回來。
 *
 * ── 為什麼需要它 ──
 * 匿名訪客整段流程都走得到「送出訂單」（加入購物車、詢價、選門市都不需要登入），
 * 到最後一步才撞 401。FE-25 已經給了一條回得來的路（`?next=/checkout`），
 * 但回來的是一張**空表單**：配送方式變回「尚未選擇」、門市代號與留言全沒了。
 * 「路是有的，但走回來之後東西不見了」是同一個病的下一節——人要重填一次才知道。
 *
 * ── 為什麼是 `sessionStorage` 而不是 `localStorage` ──
 * 這是一份**只在這一趟旅程裡有意義**的暫存：分頁關掉就該消失。
 * `localStorage` 會讓「三天前那次沒結成的帳」在今天重新開站時冒出來，
 * 把舊的門市代號與留言默默塞進一張新的單子——那是畫面宣稱了不成立的事。
 *
 * ── 為什麼鍵含 cart id、值裡也再記一次 ──
 * 購物車換了一張（登出後訪客拿到新車、或後端換發 `gg_cart`）就不該還原：
 * 上一張車的門市代號跟這一張沒有關係。鍵含 cart id 讓不同的車天生互不干擾；
 * 值裡再記一次是**第二道**——鍵可能被別的東西寫過，內容自己說得出它屬於誰才算數。
 *
 * ── 只存五個欄位，不存整份購物車 ──
 * 購物車的真相在後端（價格、庫存、可否購買都會變），存一份複本回來覆蓋
 * 就是拿舊資料畫新畫面。這裡只存**使用者自己填的東西**，其餘一律重新跟後端要。
 */
import type { components } from '@greygray/api-client/storefront';
import { DELIVERY_METHOD_LABEL, SHIPPING_POLICY_LABEL } from './labels';

type S = components['schemas'];

/** 被 401 彈走時要保住的五個欄位。 */
export interface CheckoutDraft {
  readonly deliveryMethod: S['DeliveryMethod'] | null;
  readonly shippingPolicy: S['ShippingPolicy'] | null;
  readonly shippingAddressId: string | null;
  readonly convenienceStoreCode: string;
  readonly buyerNote: string;
}

/** 鍵的前綴。掃「還有沒有別張車的殘留」時也用它。 */
export const CHECKOUT_DRAFT_KEY_PREFIX = 'gg:checkout-draft:';

export function checkoutDraftKey(cartId: string): string {
  return `${CHECKOUT_DRAFT_KEY_PREFIX}${cartId}`;
}

/**
 * `sessionStorage` 的最小介面。
 * 抽出來是為了測試餵得進一個假的——這個 workspace 沒有 jsdom，
 * 真的 `sessionStorage` 在測試環境裡根本不存在。
 */
export interface DraftStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
  readonly length: number;
  key(index: number): string | null;
}

const EMPTY_DRAFT: CheckoutDraft = {
  deliveryMethod: null,
  shippingPolicy: null,
  shippingAddressId: null,
  convenienceStoreCode: '',
  buyerNote: '',
};

/** 草稿裡完全沒有使用者填過的東西時就不必存——空的草稿還原起來跟沒有一樣。 */
export function isEmptyDraft(draft: CheckoutDraft): boolean {
  return (
    draft.deliveryMethod === null &&
    draft.shippingPolicy === null &&
    draft.shippingAddressId === null &&
    draft.convenienceStoreCode === '' &&
    draft.buyerNote === ''
  );
}

export function serializeCheckoutDraft(cartId: string, draft: CheckoutDraft): string {
  return JSON.stringify({
    cartId,
    deliveryMethod: draft.deliveryMethod,
    shippingPolicy: draft.shippingPolicy,
    shippingAddressId: draft.shippingAddressId,
    convenienceStoreCode: draft.convenienceStoreCode,
    buyerNote: draft.buyerNote,
  });
}

function asEnum<T extends string>(value: unknown, allowed: Record<T, unknown>): T | null {
  return typeof value === 'string' && Object.hasOwn(allowed, value) ? (value as T) : null;
}

function asNullableString(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * 把存起來的字串解回草稿。**任何不對勁都回 `null`（＝當作沒有草稿）**：
 * 壞掉的 JSON、不是物件、屬於別張車、欄位型別不對的列舉值。
 *
 * 不做「盡量救回一部分」——一份自己都對不上的草稿還原出來，
 * 使用者會看到一半是他填的、一半不是，比空白更難察覺。
 * （單一欄位型別不對時退回該欄位的空值，那是欄位層級的保守化，不是猜。）
 */
export function parseCheckoutDraft(raw: string | null | undefined, cartId: string): CheckoutDraft | null {
  if (typeof raw !== 'string' || raw === '') return null;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return null;

  const record = parsed as Record<string, unknown>;
  // 這張草稿必須說得出它屬於哪一張車，而且要是這一張。
  if (record.cartId !== cartId) return null;

  return {
    deliveryMethod: asEnum<S['DeliveryMethod']>(record.deliveryMethod, DELIVERY_METHOD_LABEL),
    shippingPolicy: asEnum<S['ShippingPolicy']>(record.shippingPolicy, SHIPPING_POLICY_LABEL),
    shippingAddressId: asNullableString(record.shippingAddressId),
    convenienceStoreCode: asString(record.convenienceStoreCode),
    buyerNote: asString(record.buyerNote),
  };
}

/**
 * 取得 `sessionStorage`。拿不到（伺服器端渲染、隱私模式）回 `null`——
 * **呼叫端一律把「拿不到」當成「沒有草稿」，頁面照常運作。**
 */
function defaultStorage(): DraftStorage | null {
  try {
    if (typeof globalThis === 'undefined') return null;
    const storage = (globalThis as { sessionStorage?: DraftStorage }).sessionStorage;
    return storage ?? null;
  } catch {
    // Safari 私密瀏覽在**存取這個屬性**時就會丟例外，不是等到 setItem。
    return null;
  }
}

/** 存草稿。空草稿等同清掉。任何例外都吞掉——存不起來不該擋住登入導向。 */
export function saveCheckoutDraft(
  cartId: string,
  draft: CheckoutDraft,
  storage: DraftStorage | null = defaultStorage(),
): void {
  if (!storage) return;
  try {
    if (isEmptyDraft(draft)) {
      storage.removeItem(checkoutDraftKey(cartId));
      return;
    }
    storage.setItem(checkoutDraftKey(cartId), serializeCheckoutDraft(cartId, draft));
  } catch {
    // 配額滿或隱私模式：沒存到就是沒草稿，不影響流程。
  }
}

/** 讀草稿；沒有、壞掉、屬於別張車都回 `null`。 */
export function loadCheckoutDraft(
  cartId: string,
  storage: DraftStorage | null = defaultStorage(),
): CheckoutDraft | null {
  if (!storage) return null;
  try {
    return parseCheckoutDraft(storage.getItem(checkoutDraftKey(cartId)), cartId);
  } catch {
    return null;
  }
}

/** 刪掉這張車的草稿。送出成功之後一定要叫。 */
export function clearCheckoutDraft(
  cartId: string,
  storage: DraftStorage | null = defaultStorage(),
): void {
  if (!storage) return;
  try {
    storage.removeItem(checkoutDraftKey(cartId));
  } catch {
    // 刪不掉也沒關係：下一次載入時 cart id 對不上就不會還原。
  }
}

/**
 * 掃掉**別張車**留下的草稿。
 *
 * 鍵含 cart id 已經讓不同的車互不干擾，但換車（登出→訪客拿到新車）之後
 * 舊的那筆會一直留在這個分頁的 `sessionStorage` 裡。派工書要的是
 * 「購物車 id 不同就不還原**並刪掉**」——不還原由鍵與 `cartId` 兩道守著，
 * 刪掉由這一支做。
 */
export function clearOtherCheckoutDrafts(
  cartId: string,
  storage: DraftStorage | null = defaultStorage(),
): void {
  if (!storage) return;
  try {
    const keep = checkoutDraftKey(cartId);
    const stale: string[] = [];
    for (let index = 0; index < storage.length; index += 1) {
      const key = storage.key(index);
      if (key && key.startsWith(CHECKOUT_DRAFT_KEY_PREFIX) && key !== keep) stale.push(key);
    }
    // 先收集再刪：邊走邊刪會讓 index 跳過下一筆。
    for (const key of stale) storage.removeItem(key);
  } catch {
    // 掃不動就算了，殘留的草稿不會被還原（cart id 對不上）。
  }
}

/** 給呼叫端組一份空草稿用（測試與預設值共用同一個定義）。 */
export function emptyCheckoutDraft(): CheckoutDraft {
  return EMPTY_DRAFT;
}
