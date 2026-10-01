import { ApiError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { describe, expect, it } from 'vitest';
import {
  formatPaymentDeadlineInTaipei,
  hasOutstandingInstructions,
  orderPaymentPresentation,
  paymentConflictRefreshAction,
  paymentInitiationFailureAction,
  paymentInstructionsDisplay,
  paymentOrderLoadFailureAction,
  paymentRedirectAction,
  paymentResultPresentation,
  shouldPollPaymentResult,
  shouldReloadOrderAfterCancelError,
  shouldReloadOrderAfterPaymentError,
} from '../paymentInstructions';

type S = components['schemas'];

const money = { amountMinor: 123400, currency: 'TWD' } as const;
const expiresAt = '2026-10-04T15:59:59Z';

function instructions(
  method: S['PaymentMethod'],
  fields: Partial<S['PaymentInstructions']> = {},
): S['PaymentInstructions'] {
  return { method, expiresAt, issuedAt: '2026-10-01T00:00:00Z', ...fields };
}

function displayOrder(paymentInstructions: S['PaymentInstructions'] | null | undefined) {
  return paymentInstructions === undefined ? { grandTotal: money } : { grandTotal: money, paymentInstructions };
}

function paymentOrder(paymentInstructions: S['PaymentInstructions'] | null | undefined) {
  const base = {
    status: 'AwaitingPayment' as const,
    paymentDueAt: expiresAt,
  };
  return paymentInstructions === undefined ? base : { ...base, paymentInstructions };
}

function apiError(status: number, code: string): ApiError {
  return new ApiError({
    type: `https://greygray.tw/errors/${code}`,
    title: code,
    status,
    code,
  });
}

describe('FE-57 T1：取號資訊存在判斷', () => {
  it('有值為 true；null 與 undefined 都是 false', () => {
    expect(hasOutstandingInstructions({ paymentInstructions: instructions('Atm') })).toBe(true);
    expect(hasOutstandingInstructions({ paymentInstructions: null })).toBe(false);
    expect(hasOutstandingInstructions({})).toBe(false);
  });
});

describe('FE-57 T2：取號顯示資料', () => {
  it('ATM 有銀行、可複製帳號、後端含運總額與台北期限', () => {
    const display = paymentInstructionsDisplay(
      displayOrder(instructions('Atm', { bankCode: '822', virtualAccount: '9912345678901234' })),
    );
    expect(display?.title).toBe('請在期限內完成 ATM 轉帳');
    expect(display?.rows).toEqual([
      { key: 'bank-code', label: '銀行代碼', value: '822', copyable: false },
      { key: 'virtual-account', label: '轉帳帳號', value: '9912345678901234', copyable: true },
      { key: 'amount', label: '應繳金額', value: 'NT$1,234', copyable: false },
      { key: 'deadline', label: '繳費期限', value: '2026/10/04 23:59（台灣時間）', copyable: false },
    ]);
  });

  it('超商代碼可複製，條碼依原順序且不畫圖所需資料', () => {
    const code = paymentInstructionsDisplay(
      displayOrder(instructions('ConvenienceStoreCode', { paymentNo: 'CVS123456' })),
    );
    expect(code?.title).toContain('超商多媒體機台');
    expect(code?.rows[0]).toEqual({ key: 'payment-no', label: '繳費代碼', value: 'CVS123456', copyable: true });

    const barcode = paymentInstructionsDisplay(
      displayOrder(instructions('Barcode', { barcodes: ['BAR-1', 'BAR-2', 'BAR-3'] })),
    );
    expect(barcode?.title).toContain('以下三段條碼');
    expect(barcode?.rows.slice(0, 3).map((row) => [row.label, row.value])).toEqual([
      ['條碼 1', 'BAR-1'],
      ['條碼 2', 'BAR-2'],
      ['條碼 3', 'BAR-3'],
    ]);
  });

  it('固定 UTC 輸入轉台北時間；未知 method 顯示通用句且不丟例外', () => {
    expect(formatPaymentDeadlineInTaipei(expiresAt)).toBe('2026/10/04 23:59（台灣時間）');
    const unknown = instructions('Unknown' as S['PaymentMethod']);
    expect(() => paymentInstructionsDisplay(displayOrder(unknown))).not.toThrow();
    expect(paymentInstructionsDisplay(displayOrder(unknown))?.title).toBe('請依綠界提供的繳費資訊完成付款');
  });
});

describe('FE-57 T4：付款結果頁判斷', () => {
  it('有取號資訊停止輪詢且只顯示取號結果需要的區塊', () => {
    const order = paymentOrder(instructions('Atm'));
    expect(shouldPollPaymentResult(order, 0)).toBe(false);
    expect(paymentResultPresentation(order, false)).toEqual({
      hasInstructions: true,
      title: '已產生繳費資訊',
      showConfirmingMessage: false,
      showUnconfirmedMessage: false,
      showRetryPayment: false,
      showManualRefresh: false,
      showPrice: false,
    });
  });

  it('無取號資訊維持輪詢、確認文案、付款與重查流程', () => {
    const order = paymentOrder(undefined);
    expect(shouldPollPaymentResult(order, 0)).toBe(true);
    expect(paymentResultPresentation(order, false)).toMatchObject({
      showConfirmingMessage: true,
      showRetryPayment: true,
      showPrice: true,
    });
    expect(paymentResultPresentation(order, true)).toMatchObject({
      showUnconfirmedMessage: true,
      showManualRefresh: true,
    });
  });

  it('FE-61 T3：逾期優先停止輪詢，只顯示確認中所需欄位', () => {
    const order = { ...paymentOrder(instructions('Atm')), paymentOverdue: true };
    expect(shouldPollPaymentResult(order, 0)).toBe(false);
    expect(paymentResultPresentation(order, false)).toEqual({
      hasInstructions: false,
      title: '繳費期限已過，正在確認付款',
      showConfirmingMessage: false,
      showUnconfirmedMessage: false,
      showRetryPayment: false,
      showManualRefresh: false,
      showPrice: false,
      showOverdue: true,
    });
  });
});

describe('FE-57 T5：付款發動頁判斷', () => {
  it('有取號資訊不發動；沒有才發動', () => {
    expect(paymentRedirectAction({ paymentInstructions: instructions('Atm') })).toBe('show-instructions');
    expect(paymentRedirectAction({})).toBe('initiate-payment');
  });

  it('FE-61 T2：逾期優先於既有取號資訊', () => {
    const overdue = { paymentInstructions: instructions('Atm'), paymentOverdue: true };
    expect(paymentRedirectAction(overdue)).toBe('show-overdue');
    expect(paymentConflictRefreshAction(overdue)).toBe('show-overdue');
  });

  it('讀訂單 401 去登入，其他錯誤照常發動', () => {
    expect(paymentOrderLoadFailureAction(apiError(401, 'auth.unauthorized'))).toBe('login');
    expect(paymentOrderLoadFailureAction(apiError(500, 'platform.unexpected'))).toBe('initiate-payment');
  });

  it('取號衝突先重讀；重讀有資訊顯示卡，仍無資訊顯示錯誤', () => {
    const conflict = apiError(409, 'payment.instructions-already-issued');
    expect(paymentInitiationFailureAction(conflict)).toBe('reload-order');
    expect(paymentConflictRefreshAction({ paymentInstructions: instructions('Atm') })).toBe('show-instructions');
    expect(paymentConflictRefreshAction({ paymentInstructions: null })).toBe('show-error');
  });

  it('FE-61 T2：付款逾期錯誤先重讀，重讀後顯示確認中', () => {
    const overdue = apiError(422, 'ordering.payment-overdue');
    expect(paymentInitiationFailureAction(overdue)).toBe('reload-order');
    expect(paymentConflictRefreshAction({ paymentInstructions: null, paymentOverdue: true })).toBe('show-overdue');
  });
});

describe('FE-57 T6：訂單詳情付款判斷', () => {
  it('有取號資訊不能再付款，但待付款倒數仍顯示', () => {
    expect(orderPaymentPresentation(paymentOrder(instructions('Atm')))).toEqual({
      canPay: false,
      showCountdown: true,
    });
  });

  it('FE-61 T6：取消同時更新要重讀，其他取消失敗不重讀', () => {
    expect(shouldReloadOrderAfterCancelError(apiError(409, 'ordering.concurrent-update'))).toBe(true);
    expect(shouldReloadOrderAfterCancelError(apiError(422, 'ordering.concurrent-update'))).toBe(false);
    expect(shouldReloadOrderAfterCancelError(apiError(422, 'ordering.cannot-self-cancel-after-payment'))).toBe(false);
  });

  it('payment.instructions-already-issued 要重讀，其他錯誤不重讀', () => {
    expect(shouldReloadOrderAfterPaymentError(apiError(409, 'payment.instructions-already-issued'))).toBe(true);
    expect(shouldReloadOrderAfterPaymentError(apiError(422, 'ordering.payment-overdue'))).toBe(true);
    expect(shouldReloadOrderAfterPaymentError(apiError(409, 'ordering.payment-overdue'))).toBe(false);
    expect(shouldReloadOrderAfterPaymentError(apiError(409, 'ordering.order-cancelled'))).toBe(false);
  });

  it('FE-61 T1：逾期不能付款、不顯示倒數，只在逾期結果新增 showOverdue', () => {
    expect(orderPaymentPresentation({ ...paymentOrder(undefined), paymentOverdue: true })).toEqual({
      canPay: false,
      showCountdown: false,
      showOverdue: true,
    });
    expect(orderPaymentPresentation(paymentOrder(undefined))).toEqual({
      canPay: true,
      showCountdown: true,
    });
  });
});
