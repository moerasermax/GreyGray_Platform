/** 收件地址表單欄位，鏡射 `ShippingAddressInput`（`docs/api/openapi.storefront.yaml`）。 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface AddressFormValues {
  recipientName: string;
  phoneNumber: string;
  postalCode: string;
  city: string;
  district: string;
  streetAddress: string;
  isDefault: boolean;
}

export const ADDRESS_INITIAL_VALUES: AddressFormValues = {
  recipientName: '',
  phoneNumber: '',
  postalCode: '',
  city: '',
  district: '',
  streetAddress: '',
  isDefault: false,
};

export type AddressFieldErrors = Partial<Record<keyof AddressFormValues, string>>;

export function addressValuesFrom(address: S['ShippingAddress']): AddressFormValues {
  return {
    recipientName: address.recipientName,
    phoneNumber: address.phoneNumber,
    postalCode: address.postalCode,
    city: address.city,
    district: address.district,
    streetAddress: address.streetAddress,
    isDefault: address.isDefault ?? false,
  };
}

/** 送出前的前端預檢，鏡射契約裡的欄位長度限制。 */
export function validateAddressForm(values: AddressFormValues): AddressFieldErrors {
  const errors: AddressFieldErrors = {};

  if (!values.recipientName.trim()) {
    errors.recipientName = '請輸入收件人姓名。';
  } else if (values.recipientName.length > 50) {
    errors.recipientName = '收件人姓名不能超過 50 個字。';
  }
  if (!values.phoneNumber.trim()) {
    errors.phoneNumber = '請輸入聯絡電話。';
  } else if (values.phoneNumber.length > 20) {
    errors.phoneNumber = '聯絡電話不能超過 20 個字。';
  }
  if (!values.postalCode.trim()) {
    errors.postalCode = '請輸入郵遞區號。';
  } else if (values.postalCode.length > 6) {
    errors.postalCode = '郵遞區號不能超過 6 碼。';
  }
  if (!values.city.trim()) {
    errors.city = '請輸入縣市。';
  } else if (values.city.length > 20) {
    errors.city = '縣市不能超過 20 個字。';
  }
  if (!values.district.trim()) {
    errors.district = '請輸入鄉鎮市區。';
  } else if (values.district.length > 20) {
    errors.district = '鄉鎮市區不能超過 20 個字。';
  }
  if (!values.streetAddress.trim()) {
    errors.streetAddress = '請輸入詳細地址。';
  } else if (values.streetAddress.length > 200) {
    errors.streetAddress = '詳細地址不能超過 200 個字。';
  }

  return errors;
}

export function toAddressInput(values: AddressFormValues): S['ShippingAddressInput'] {
  return {
    recipientName: values.recipientName,
    phoneNumber: values.phoneNumber,
    postalCode: values.postalCode,
    city: values.city,
    district: values.district,
    streetAddress: values.streetAddress,
    isDefault: values.isDefault,
  };
}
