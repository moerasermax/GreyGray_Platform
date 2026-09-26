import { Field, Input } from '@greygray/ui';
import type { AddressFieldErrors, AddressFormValues } from '../_lib/addressSchema';

export interface AddressFormProps {
  values: AddressFormValues;
  errors: AddressFieldErrors;
  onChange: (values: AddressFormValues) => void;
}

/** 收件地址表單欄位。新增與編輯共用同一組欄位，避免兩份會慢慢長歪。 */
export function AddressForm({ values, errors, onChange }: AddressFormProps) {
  function setField<K extends keyof AddressFormValues>(key: K, value: AddressFormValues[K]) {
    onChange({ ...values, [key]: value });
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-4)]">
      <Field label="收件人姓名" htmlFor="address-recipient-name" required error={errors.recipientName}>
        <Input
          id="address-recipient-name"
          invalid={Boolean(errors.recipientName)}
          value={values.recipientName}
          onChange={(e) => setField('recipientName', e.target.value)}
        />
      </Field>

      <Field label="聯絡電話" htmlFor="address-phone" required error={errors.phoneNumber}>
        <Input
          id="address-phone"
          type="tel"
          invalid={Boolean(errors.phoneNumber)}
          value={values.phoneNumber}
          onChange={(e) => setField('phoneNumber', e.target.value)}
        />
      </Field>

      <div className="grid grid-cols-2 gap-[var(--gg-space-3)]">
        <Field label="郵遞區號" htmlFor="address-postal-code" required error={errors.postalCode}>
          <Input
            id="address-postal-code"
            inputMode="numeric"
            invalid={Boolean(errors.postalCode)}
            value={values.postalCode}
            onChange={(e) => setField('postalCode', e.target.value)}
          />
        </Field>
        <Field label="縣市" htmlFor="address-city" required error={errors.city}>
          <Input
            id="address-city"
            invalid={Boolean(errors.city)}
            value={values.city}
            onChange={(e) => setField('city', e.target.value)}
          />
        </Field>
      </div>

      <Field label="鄉鎮市區" htmlFor="address-district" required error={errors.district}>
        <Input
          id="address-district"
          invalid={Boolean(errors.district)}
          value={values.district}
          onChange={(e) => setField('district', e.target.value)}
        />
      </Field>

      <Field label="詳細地址" htmlFor="address-street" required error={errors.streetAddress}>
        <Input
          id="address-street"
          invalid={Boolean(errors.streetAddress)}
          value={values.streetAddress}
          onChange={(e) => setField('streetAddress', e.target.value)}
        />
      </Field>

      <label className="flex items-center gap-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] text-fg">
        <input
          type="checkbox"
          className="h-[var(--gg-space-4)] w-[var(--gg-space-4)] accent-primary-strong"
          checked={values.isDefault}
          onChange={(e) => setField('isDefault', e.target.checked)}
        />
        設為預設地址
      </label>
    </div>
  );
}
