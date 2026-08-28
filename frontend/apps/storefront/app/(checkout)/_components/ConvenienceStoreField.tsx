'use client';

import { Field, Input } from '@greygray/ui';

export interface ConvenienceStoreFieldProps {
  value: string;
  onChange: (code: string) => void;
}

/**
 * **假設，未接真正的綠界電子地圖**（見交付回報「假設」一節）。
 * 契約 `convenienceStoreCode` 的說明是「綠界電子地圖回傳的門市代號」——
 * 那是一個會跳出 iframe／彈窗選門市的外部元件，這裡先用文字輸入代替，
 * 只收 `convenienceStoreCode` 這一個值，跟 `CheckoutRequest` 的欄位一致。
 */
export function ConvenienceStoreField({ value, onChange }: ConvenienceStoreFieldProps) {
  return (
    <Field
      label="取貨門市代號"
      htmlFor="checkout-store-code"
      required
      hint="正式串接綠界電子地圖後這欄會自動帶入，現在先手動輸入門市代號。"
    >
      <Input
        id="checkout-store-code"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="991234"
      />
    </Field>
  );
}
