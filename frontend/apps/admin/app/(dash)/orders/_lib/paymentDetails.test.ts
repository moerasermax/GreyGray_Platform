import { ApiError } from '@greygray/api-client';
import { describe, expect, it } from 'vitest';
import {
  canRecordManualRefund,
  formatRemittedOn,
  formatTaipeiDateTime,
  manualRefundErrorMessage,
  paymentInstructionRows,
  taipeiToday,
  validateManualRefundInput,
} from './paymentDetails';

describe('台北時間', () => {
  it('UTC 15:59:59 轉成同日 23:59，固定 24 小時制', () => {
    expect(formatTaipeiDateTime('2026-10-04T15:59:59Z')).toBe('2026/10/04 23:59（台灣時間）');
  });

  it('UTC 16:00 跨到台北隔日', () => {
    expect(taipeiToday(new Date('2026-10-04T15:59:59Z'))).toBe('2026-10-04');
    expect(taipeiToday(new Date('2026-10-04T16:00:00Z'))).toBe('2026-10-05');
  });

  it('純日期只換分隔符，不經 Date', () => {
    expect(formatRemittedOn('2026-10-05')).toBe('2026/10/05');
  });
});

describe('人工退款權限', () => {
  it.each(['Owner', 'Operator', 'Accountant'] as const)('%s 可以登記', (role) => {
    expect(canRecordManualRefund(role)).toBe(true);
  });
  it.each(['ReadOnly', 'UnknownRole'])('%s 不可登記', (role) => {
    expect(canRecordManualRefund(role)).toBe(false);
  });
});

describe('人工退款表單', () => {
  const now = new Date('2026-10-04T16:00:00Z');

  it('整數元用 Money helper 轉成 amountMinor', () => {
    expect(validateManualRefundInput('500', '2026-10-05', '', now)).toEqual({
      ok: true,
      body: { amount: { amountMinor: 50_000, currency: 'TWD' }, remittedOn: '2026-10-05', note: null },
    });
  });

  it.each(['10.5', '1,000', '0', '-5'])('拒絕非正整數元：%s', (amount) => {
    expect(validateManualRefundInput(amount, '2026-10-05', '', now)).toMatchObject({ ok: false });
  });

  it('拒絕台北明天與 201 字備註，接受 200 字', () => {
    expect(validateManualRefundInput('1', '2026-10-06', '', now)).toMatchObject({ ok: false });
    expect(validateManualRefundInput('1', '2026-10-05', 'a'.repeat(200), now)).toMatchObject({ ok: true });
    expect(validateManualRefundInput('1', '2026-10-05', 'a'.repeat(201), now)).toMatchObject({ ok: false });
  });
});

describe('人工退款錯誤訊息', () => {
  function error(status: number, code: string): ApiError {
    return new ApiError({ type: 'about:blank', title: '原始錯誤', status, code });
  }

  it('每個業務錯誤都有固定訊息', () => {
    expect(manualRefundErrorMessage(error(422, 'payment.manual-refund-exceeds-outstanding'))).toContain('超過');
    expect(manualRefundErrorMessage(error(422, 'payment.manual-refund-not-required'))).toContain('不需要');
    expect(manualRefundErrorMessage(error(422, 'payment.manual-refund-amount-invalid'))).toContain('整數元');
    expect(manualRefundErrorMessage(error(422, 'payment.manual-refund-date-invalid'))).toContain('台灣時間');
    expect(manualRefundErrorMessage(error(409, 'payment.concurrent-update'))).toContain('另一筆');
  });

  it('404 依 code 分流，其他錯誤交回既有處理', () => {
    expect(manualRefundErrorMessage(error(404, 'platform.not-found'))).toContain('找不到');
    expect(manualRefundErrorMessage(error(404, 'platform.route-not-found'))).toContain('尚未開放');
    expect(manualRefundErrorMessage(error(500, 'platform.unexpected'))).toBeNull();
    expect(manualRefundErrorMessage(new Error('network'))).toBeNull();
  });
});

describe('取號資料列', () => {
  const common = { expiresAt: '2026-10-04T15:59:59Z', issuedAt: '2026-10-01T02:00:00Z' };
  it('ATM、超商代碼、條碼各自只列契約指定欄位', () => {
    expect(paymentInstructionRows({ method: 'Atm', bankCode: '822', virtualAccount: '12345', ...common }).map((r) => r.label)).toEqual(['銀行代碼', '虛擬帳號', '繳費期限', '取號時間']);
    expect(paymentInstructionRows({ method: 'ConvenienceStoreCode', paymentNo: 'CVS001', ...common }).map((r) => r.label)).toContain('繳費代碼');
    expect(paymentInstructionRows({ method: 'Barcode', barcodes: ['A', 'B', 'C'], ...common }).map((r) => r.value)).toEqual(expect.arrayContaining(['A', 'B', 'C']));
  });
  it('null 與缺席欄位不丟例外', () => {
    expect(paymentInstructionRows(null)).toEqual([]);
    expect(paymentInstructionRows({ method: 'Atm', ...common })).toHaveLength(2);
  });
});
