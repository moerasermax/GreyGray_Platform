import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const libDirectory = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const orderPage = readFileSync(join(libDirectory, '..', '(account)', 'orders', '[orderId]', 'page.tsx'), 'utf8');
const paymentPage = readFileSync(join(libDirectory, 'payment', '[orderId]', 'page.tsx'), 'utf8');
const resultPage = readFileSync(join(libDirectory, 'payment', 'result', 'page.tsx'), 'utf8');

describe('FE-61 T2／T3／T6：付款頁原始碼守衛', () => {
  it('付款發動頁在初讀與錯誤重讀後都接住 show-overdue', () => {
    expect(paymentPage.match(/=== 'show-overdue'/g)).toHaveLength(2);
    expect(paymentPage.indexOf('order?.paymentOverdue === true')).toBeLessThan(paymentPage.indexOf('order?.paymentInstructions != null'));
  });

  it('結果頁不對逾期設 pollExhausted，且不再承諾原路退還', () => {
    expect(resultPage).toContain('order.paymentOverdue !== true');
    expect(resultPage).toContain('<PaymentOverdueNotice />');
    expect(resultPage).not.toContain('原路退還');
  });

  it('詳情頁把付款重讀放回 handlePay，取消併發則由 handleCancel 重讀', () => {
    const cancelBody = orderPage.slice(orderPage.indexOf('async function handleCancel'), orderPage.indexOf('async function handlePay'));
    const payBody = orderPage.slice(orderPage.indexOf('async function handlePay'), orderPage.indexOf('function handlePaymentCountdownExpire'));
    expect(cancelBody).toContain('shouldReloadOrderAfterCancelError(caught)');
    expect(cancelBody).not.toContain('shouldReloadOrderAfterPaymentError(caught)');
    expect(payBody).toContain('shouldReloadOrderAfterPaymentError(caught)');
    expect(payBody).toContain('paymentIdempotency.complete()');
    expect(payBody).toContain('await load(true)');
  });

  it('倒數到期用背景重讀，並以同張訂單同一期限的 key 擋重複', () => {
    expect(orderPage).toContain('if (!background) setLoading(true)');
    expect(orderPage).toContain('if (expiredPaymentKey === key) return');
    expect(orderPage).toContain('付款期限已到，系統處理中');
    expect(orderPage).not.toContain('onExpire={load}');
  });
});
