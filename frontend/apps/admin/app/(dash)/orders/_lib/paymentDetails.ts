import { ApiError, moneyFromMajorInput } from '@greygray/api-client';
import type { components, paths } from '@greygray/api-client/admin';
import { hasRequiredRole, type StaffRole } from '../../../login/_lib/session';

type S = components['schemas'];
export type RecordManualRefundRequest = paths['/v1/orders/{orderId}/payments/{paymentId}/manual-refunds']['post']['requestBody']['content']['application/json'];

function taipeiParts(date: Date): Record<string, string> {
  return Object.fromEntries(
    new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Taipei',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    })
      .formatToParts(date)
      .filter((part) => part.type !== 'literal')
      .map((part) => [part.type, part.value]),
  );
}

export function formatTaipeiDateTime(value: string | Date): string {
  const parts = taipeiParts(value instanceof Date ? value : new Date(value));
  return `${parts.year}/${parts.month}/${parts.day} ${parts.hour}:${parts.minute}（台灣時間）`;
}

export function taipeiToday(now: Date): string {
  const parts = taipeiParts(now);
  return `${parts.year}-${parts.month}-${parts.day}`;
}

export function formatRemittedOn(value: string): string {
  return value.replaceAll('-', '/');
}

export function canRecordManualRefund(role: StaffRole | (string & {})): boolean {
  const knownRole = role as StaffRole;
  return hasRequiredRole(knownRole, 'Operator') || hasRequiredRole(knownRole, 'Accountant');
}

export type ManualRefundValidation =
  | { readonly ok: true; readonly body: RecordManualRefundRequest }
  | { readonly ok: false; readonly error: string };

export function validateManualRefundInput(
  amountInput: string,
  remittedOn: string,
  noteInput: string,
  now: Date,
): ManualRefundValidation {
  const amountText = amountInput.trim();
  if (!/^\d+$/.test(amountText) || amountText === '0') {
    return { ok: false, error: '金額必須是大於 0 的整數元。' };
  }
  const amount = moneyFromMajorInput(amountText, 'TWD');
  if (!amount || amount.amountMinor <= 0) {
    return { ok: false, error: '金額必須是大於 0 的整數元。' };
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(remittedOn) || remittedOn > taipeiToday(now)) {
    return { ok: false, error: '匯款日期不能晚於今天（台灣時間）。' };
  }
  const note = noteInput.trim();
  if (note.length > 200) {
    return { ok: false, error: '備註最多 200 字。' };
  }
  return { ok: true, body: { amount, remittedOn, note: note || null } };
}

export function manualRefundErrorMessage(cause: unknown): string | null {
  if (!(cause instanceof ApiError)) return null;
  switch (cause.code) {
    case 'payment.manual-refund-exceeds-outstanding':
      return '超過尚待匯出的金額。';
    case 'payment.manual-refund-not-required':
      return '這筆付款目前不需要人工退款，請重新整理。';
    case 'payment.manual-refund-amount-invalid':
      return '金額必須是大於 0 的整數元。';
    case 'payment.manual-refund-date-invalid':
      return '匯款日期不能晚於今天（台灣時間）。';
    case 'payment.concurrent-update':
      return '剛好有另一筆登記，請重新整理後再試。';
    case 'platform.not-found':
      return cause.status === 404 ? '找不到這筆訂單或付款，請重新整理。' : null;
    default:
      return cause.status === 404 ? '後端尚未開放人工退款登記。' : null;
  }
}

export interface PaymentInstructionRow {
  readonly label: string;
  readonly value: string;
}

export function paymentInstructionRows(instructions: S['PaymentInstructions'] | null | undefined): readonly PaymentInstructionRow[] {
  if (!instructions) return [];
  const rows: PaymentInstructionRow[] = [];
  if (instructions.method === 'Atm') {
    if (instructions.bankCode) rows.push({ label: '銀行代碼', value: instructions.bankCode });
    if (instructions.virtualAccount) rows.push({ label: '虛擬帳號', value: instructions.virtualAccount });
  } else if (instructions.method === 'ConvenienceStoreCode') {
    if (instructions.paymentNo) rows.push({ label: '繳費代碼', value: instructions.paymentNo });
  } else if (instructions.method === 'Barcode') {
    for (const [index, barcode] of (instructions.barcodes ?? []).entries()) {
      rows.push({ label: `條碼 ${index + 1}`, value: barcode });
    }
  }
  rows.push({ label: '繳費期限', value: formatTaipeiDateTime(instructions.expiresAt) });
  rows.push({ label: '取號時間', value: formatTaipeiDateTime(instructions.issuedAt) });
  return rows;
}
