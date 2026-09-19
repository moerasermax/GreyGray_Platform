/**
 * 結帳頁收件人姓名與手機（ADR-039，超商取貨專用）。
 *
 * 手機格式比 `(account)/_lib/addressSchema.ts` 的 `validateAddressForm` 嚴——
 * 那支只檢查必填與長度 ≤ 20，沒有格式檢查。這裡要嚴，因為綠界與超商會核對格式，
 * 格式錯客人就拿不到貨。
 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

/** `09` 開頭、共十碼。 */
export const RECIPIENT_PHONE_PATTERN = /^09\d{8}$/;

export function isRecipientNameFilled(recipientName: string): boolean {
  return recipientName.trim() !== '';
}

export function isRecipientPhoneValid(recipientPhone: string): boolean {
  return RECIPIENT_PHONE_PATTERN.test(recipientPhone.trim());
}

/**
 * 只有超商取貨才送收件人姓名手機；宅配送了也會被後端忽略（改從地址簿抄），
 * 前端乾脆不送，避免看起來像是這兩個欄位對宅配也有效。
 */
export function checkoutRecipientPayload(
  deliveryMethod: S['DeliveryMethod'] | null,
  recipientName: string,
  recipientPhone: string,
): { recipientName: string | null; recipientPhone: string | null } {
  if (deliveryMethod !== 'ConvenienceStore') {
    return { recipientName: null, recipientPhone: null };
  }
  return { recipientName, recipientPhone };
}
