/**
 * 純函式規則。跟 React 無關，方便單獨測試。
 * **不做任何金額運算**——這裡只做「能不能結帳」的判斷，數字一律讀後端回的欄位。
 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface CartLinesLike {
  readonly lines: readonly S['CartLine'][];
}

export interface HasMixedModesLike {
  readonly hasMixedModes: boolean;
}

/**
 * 購物車裡只要有一項 `availabilityWarning`，結帳鈕就要被擋下。
 * 回傳第一個警告訊息給畫面顯示；沒有就回 `null`（可以結帳）。
 */
export function blockingAvailabilityWarning(cart: CartLinesLike): string | null {
  const line = cart.lines.find((l) => l.availabilityWarning != null);
  return line?.availabilityWarning ?? null;
}

/** `hasMixedModes` 為 true 時，結帳前必須讓客人選 `shippingPolicy`。 */
export function requiresShippingPolicyChoice(cart: HasMixedModesLike): boolean {
  return cart.hasMixedModes;
}

export function requiresShippingAddress(deliveryMethod: S['DeliveryMethod']): boolean {
  return deliveryMethod === 'HomeDelivery';
}

export function requiresConvenienceStore(deliveryMethod: S['DeliveryMethod']): boolean {
  return deliveryMethod === 'ConvenienceStore';
}

/**
 * 「讀到了門市」才算選好（ADR-038）。**不是「有選店票 id」**：
 * 讀取中、讀失敗、或畫面上的門市屬於另一張票，都不能送——客人看不到要寄到哪一家。
 */
export function hasLoadedConvenienceStore(
  selectionId: string | null,
  selection: Pick<S['CvsStoreSelection'], 'selectionId'> | null,
): boolean {
  return selectionId !== null && selection !== null && selection.selectionId === selectionId;
}

export interface CheckoutReadiness {
  readonly ready: boolean;
  /** 擋下結帳的原因，給畫面直接顯示。`ready` 為 true 時是 `null`。 */
  readonly reason: string | null;
}

export interface CheckoutReadinessInput {
  readonly cart: CartLinesLike & HasMixedModesLike;
  readonly deliveryMethod: S['DeliveryMethod'] | null;
  readonly shippingPolicy: S['ShippingPolicy'] | null;
  readonly shippingAddressId: string | null;
  readonly convenienceStoreSelectionId: string | null;
  /** 讀票讀到的門市；讀取中或讀失敗是 `null`。 */
  readonly convenienceStoreSelection: Pick<S['CvsStoreSelection'], 'selectionId'> | null;
}

/** 結帳頁「送出」鈕能不能按，以及按不了的原因——集中在一處，畫面只負責顯示。 */
export function evaluateCheckoutReadiness(input: CheckoutReadinessInput): CheckoutReadiness {
  if (input.cart.lines.length === 0) {
    return { ready: false, reason: '購物車是空的。' };
  }

  const warning = blockingAvailabilityWarning(input.cart);
  if (warning) {
    return { ready: false, reason: warning };
  }

  if (!input.deliveryMethod) {
    return { ready: false, reason: '請先選擇配送方式。' };
  }

  if (requiresShippingPolicyChoice(input.cart) && !input.shippingPolicy) {
    return { ready: false, reason: '購物車同時有現貨與預購商品，請先選擇出貨方式。' };
  }

  if (requiresShippingAddress(input.deliveryMethod) && !input.shippingAddressId) {
    return { ready: false, reason: '請選擇收件地址。' };
  }

  if (
    requiresConvenienceStore(input.deliveryMethod) &&
    !hasLoadedConvenienceStore(input.convenienceStoreSelectionId, input.convenienceStoreSelection)
  ) {
    return { ready: false, reason: '請選擇取貨門市。' };
  }

  return { ready: true, reason: null };
}
