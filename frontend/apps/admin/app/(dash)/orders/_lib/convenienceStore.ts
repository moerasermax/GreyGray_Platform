/**
 * 後台訂單詳情「取貨門市」那一格的文字（ADR-038）。出貨的人要知道寄到哪一家。
 *
 * 生成型別裡三個欄位都是「可省略又可為 null」：**`undefined` 與 `null` 同等對待**，
 * 空字串也當作沒有。代號沒有就不畫括號——不准出現「信義門市（undefined）」。
 * ADR-038 之前的舊訂單只有代號，所以「只有代號」是正常的形狀，不是資料壞掉。
 */
import type { components } from '@greygray/api-client/admin';

type AdminOrder = components['schemas']['AdminOrder'];

/** 生成型別的三個欄位，另外明確接受 `undefined`（`exactOptionalPropertyTypes` 下「省略」與「寫 undefined」是兩回事）。 */
export type ConvenienceStoreFields = {
  readonly [K in 'convenienceStoreCode' | 'convenienceStoreName' | 'convenienceStoreAddress']?: AdminOrder[K] | undefined;
};

export interface ConvenienceStoreDisplay {
  /** 名稱（代號）／只有名稱／只有代號；三個都沒有時是「未記錄門市」；只有地址時是 `null`。 */
  readonly primary: string | null;
  readonly address: string | null;
}

export const UNRECORDED_STORE_TEXT = '未記錄門市';

function present(value: string | null | undefined): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

export function convenienceStoreDisplay(order: ConvenienceStoreFields): ConvenienceStoreDisplay {
  const code = present(order.convenienceStoreCode);
  const name = present(order.convenienceStoreName);
  const address = present(order.convenienceStoreAddress);

  if (code === null && name === null && address === null) {
    return { primary: UNRECORDED_STORE_TEXT, address: null };
  }

  const primary = name !== null && code !== null ? `${name}（${code}）` : (name ?? code);
  return { primary, address };
}
