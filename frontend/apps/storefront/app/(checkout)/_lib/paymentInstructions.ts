import { ApiError, formatMoney } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { shouldKeepPolling } from './paymentResultPolling';
import { formatDateTimeInTaipei } from './paymentResultSummary';

type S = components['schemas'];
type OrderPaymentFields = Pick<S['Order'], 'status' | 'paymentDueAt' | 'paymentInstructions'>;

export interface PaymentInstructionRow {
  readonly key: string;
  readonly label: string;
  readonly value: string;
  readonly copyable: boolean;
}

export interface PaymentInstructionsDisplay {
  readonly title: string;
  readonly rows: readonly PaymentInstructionRow[];
}

export interface PaymentResultPresentation {
  readonly hasInstructions: boolean;
  readonly title: string | null;
  readonly showConfirmingMessage: boolean;
  readonly showUnconfirmedMessage: boolean;
  readonly showRetryPayment: boolean;
  readonly showManualRefresh: boolean;
  readonly showPrice: boolean;
}

export function hasOutstandingInstructions(
  order: Pick<S['Order'], 'paymentInstructions'>,
): boolean {
  return order.paymentInstructions != null;
}

export function formatPaymentDeadlineInTaipei(expiresAt: string): string {
  return `${formatDateTimeInTaipei(expiresAt)}（台灣時間）`;
}

export function paymentInstructionsDisplay(
  order: Pick<S['Order'], 'paymentInstructions' | 'grandTotal'>,
): PaymentInstructionsDisplay | null {
  const instructions = order.paymentInstructions;
  if (instructions == null) return null;

  const rows: PaymentInstructionRow[] = [];
  let title = '請依綠界提供的繳費資訊完成付款';

  switch (instructions.method as string) {
    case 'Atm':
      title = '請在期限內完成 ATM 轉帳';
      if (instructions.bankCode) {
        rows.push({ key: 'bank-code', label: '銀行代碼', value: instructions.bankCode, copyable: false });
      }
      if (instructions.virtualAccount) {
        rows.push({
          key: 'virtual-account',
          label: '轉帳帳號',
          value: instructions.virtualAccount,
          copyable: true,
        });
      }
      break;
    case 'ConvenienceStoreCode':
      title = '請到超商多媒體機台輸入繳費代碼，列印繳費單後到櫃檯繳費';
      if (instructions.paymentNo) {
        rows.push({
          key: 'payment-no',
          label: '繳費代碼',
          value: instructions.paymentNo,
          copyable: true,
        });
      }
      break;
    case 'Barcode':
      title = '請在超商櫃檯出示以下三段條碼繳費';
      (instructions.barcodes ?? []).slice(0, 3).forEach((barcode, index) => {
        rows.push({
          key: `barcode-${index + 1}`,
          label: `條碼 ${index + 1}`,
          value: barcode,
          copyable: false,
        });
      });
      break;
  }

  rows.push(
    { key: 'amount', label: '應繳金額', value: formatMoney(order.grandTotal), copyable: false },
    {
      key: 'deadline',
      label: '繳費期限',
      value: formatPaymentDeadlineInTaipei(instructions.expiresAt),
      copyable: false,
    },
  );

  return { title, rows };
}

export function shouldPollPaymentResult(order: OrderPaymentFields, attempt: number): boolean {
  return !hasOutstandingInstructions(order) && shouldKeepPolling(order.status, attempt);
}

export function paymentResultPresentation(
  order: OrderPaymentFields,
  pollExhausted: boolean,
): PaymentResultPresentation {
  const hasInstructions = hasOutstandingInstructions(order);
  const awaiting = order.status === 'AwaitingPayment';

  if (hasInstructions) {
    return {
      hasInstructions: true,
      title: '已產生繳費資訊',
      showConfirmingMessage: false,
      showUnconfirmedMessage: false,
      showRetryPayment: false,
      showManualRefresh: false,
      showPrice: false,
    };
  }

  return {
    hasInstructions: false,
    title: null,
    showConfirmingMessage: awaiting && !pollExhausted,
    showUnconfirmedMessage: awaiting && pollExhausted,
    showRetryPayment: awaiting,
    showManualRefresh: awaiting && pollExhausted,
    showPrice: true,
  };
}

export function paymentRedirectAction(
  order: Pick<S['Order'], 'paymentInstructions'>,
): 'show-instructions' | 'initiate-payment' {
  return hasOutstandingInstructions(order) ? 'show-instructions' : 'initiate-payment';
}

export function paymentOrderLoadFailureAction(error: unknown): 'login' | 'initiate-payment' {
  return error instanceof ApiError && error.isUnauthorized ? 'login' : 'initiate-payment';
}

export function isInstructionsAlreadyIssuedError(error: unknown): boolean {
  return error instanceof ApiError && error.is('payment.instructions-already-issued');
}

export function paymentInitiationFailureAction(
  error: unknown,
): 'login' | 'reload-order' | 'show-error' {
  if (error instanceof ApiError && error.isUnauthorized) return 'login';
  return isInstructionsAlreadyIssuedError(error) ? 'reload-order' : 'show-error';
}

export function paymentConflictRefreshAction(
  order: Pick<S['Order'], 'paymentInstructions'>,
): 'show-instructions' | 'show-error' {
  return hasOutstandingInstructions(order) ? 'show-instructions' : 'show-error';
}

export function orderPaymentPresentation(order: OrderPaymentFields): {
  readonly canPay: boolean;
  readonly showCountdown: boolean;
} {
  const awaiting = order.status === 'AwaitingPayment';
  return {
    canPay: awaiting && !hasOutstandingInstructions(order),
    showCountdown: awaiting && order.paymentDueAt != null,
  };
}

export function shouldReloadOrderAfterPaymentError(error: unknown): boolean {
  return isInstructionsAlreadyIssuedError(error);
}
