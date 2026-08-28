/**
 * 註冊表單欄位集中在這一個檔案（FE-5 做法要點 #1）。
 *
 * 欄位本身已經定案（ADR-019，不遷移歷史會員），不會再變；集中的理由是
 * Google 帳號串接排在 M1a 之後，屆時要在同一個註冊流程裡插入 OAuth 的入口——
 * 散在多個元件會很難改。**加 OAuth 入口時，從這裡開始找。**
 */
import type { components } from '@greygray/api-client/storefront';

type S = components['schemas'];

export interface RegisterFormValues {
  phoneNumber: string;
  password: string;
  displayName: string;
  email: string;
  referralCode: string;
}

export const REGISTER_INITIAL_VALUES: RegisterFormValues = {
  phoneNumber: '',
  password: '',
  displayName: '',
  email: '',
  referralCode: '',
};

export type RegisterFieldErrors = Partial<Record<keyof RegisterFormValues, string>>;

const PHONE_PATTERN = /^09[0-9]{8}$/;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** 送出前的前端預檢，鏡射契約裡的欄位限制。後端仍然是最終判準，422 回來一樣要能顯示。 */
export function validateRegisterForm(values: RegisterFormValues): RegisterFieldErrors {
  const errors: RegisterFieldErrors = {};

  if (!PHONE_PATTERN.test(values.phoneNumber)) {
    errors.phoneNumber = '請輸入正確格式的台灣手機號碼，例如 0912345678。';
  }
  if (values.password.length < 8 || values.password.length > 128) {
    errors.password = '密碼長度需介於 8～128 碼之間。';
  }
  if (!values.displayName.trim()) {
    errors.displayName = '請輸入顯示名稱。';
  } else if (values.displayName.length > 50) {
    errors.displayName = '顯示名稱不能超過 50 個字。';
  }
  if (values.email && !EMAIL_PATTERN.test(values.email)) {
    errors.email = 'Email 格式不正確。';
  }
  if (values.referralCode.length > 32) {
    errors.referralCode = '推薦碼不能超過 32 個字。';
  }

  return errors;
}

export function toRegisterRequest(values: RegisterFormValues): S['RegisterRequest'] {
  return {
    phoneNumber: values.phoneNumber,
    password: values.password,
    displayName: values.displayName,
    email: values.email ? values.email : null,
    referralCode: values.referralCode ? values.referralCode : null,
  };
}
