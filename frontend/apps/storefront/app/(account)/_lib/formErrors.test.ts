import { ApiError } from '@greygray/api-client';
import { describe, expect, it } from 'vitest';
import { fieldErrorsFrom, generalErrorMessage } from './formErrors';

function make422(errors: Record<string, string[]>): ApiError {
  return new ApiError({
    type: 'https://greygray.tw/errors/identity.weak-password',
    title: '欄位驗證失敗',
    status: 422,
    code: 'identity.weak-password',
    errors,
  });
}

describe('fieldErrorsFrom', () => {
  it('422 的 errors 逐一對應到欄位名，取每個欄位的第一則訊息', () => {
    const error = make422({
      phoneNumber: ['這個手機號碼已經註冊過了。'],
      password: ['密碼至少需要 8 碼，並包含英文與數字。', '第二則不應該出現'],
    });
    expect(fieldErrorsFrom(error)).toEqual({
      phoneNumber: '這個手機號碼已經註冊過了。',
      password: '密碼至少需要 8 碼，並包含英文與數字。',
    });
  });

  it('不是 ApiError 的話回傳空物件，不會炸掉呼叫端', () => {
    expect(fieldErrorsFrom(new Error('network down'))).toEqual({});
  });

  it('沒有 errors（非欄位驗證錯誤）回傳空物件', () => {
    const error = new ApiError({
      type: 'https://greygray.tw/errors/identity.invalid-credentials',
      title: '手機號碼或密碼錯誤',
      status: 401,
      code: 'identity.invalid-credentials',
    });
    expect(fieldErrorsFrom(error)).toEqual({});
  });
});

describe('generalErrorMessage', () => {
  it('ApiError 顯示 problem.title', () => {
    const error = make422({ quantity: ['數量必須大於 0。'] });
    expect(generalErrorMessage(error)).toBe('欄位驗證失敗');
  });

  it('非 ApiError 給保底訊息', () => {
    expect(generalErrorMessage(new Error('boom'))).toBe('發生非預期的錯誤，請稍後再試。');
  });
});
