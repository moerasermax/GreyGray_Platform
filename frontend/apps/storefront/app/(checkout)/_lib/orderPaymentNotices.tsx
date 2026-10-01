import type { components } from '@greygray/api-client/storefront';
import { formatDateTimeInTaipei } from './paymentResultSummary';
import { PAYMENT_OVERDUE_TITLE } from './paymentInstructions';

type S = components['schemas'];

export const PAYMENT_OVERDUE_MESSAGE = '如果你已經繳費，系統收到通知後會自動更新；未繳費的訂單將自動取消。';

type CancellationFields = Pick<S['Order'], 'paidAmount' | 'cancelledAt' | 'cancellationSource'>;

export function cancellationExplanation(order: CancellationFields): readonly string[] {
  const paid = order.paidAmount != null && order.paidAmount.amountMinor > 0;
  let main: string;

  switch (order.cancellationSource as string | null | undefined) {
    case 'PaymentExpired':
      main = paid ? '這筆訂單已由系統自動取消。' : '這筆訂單因逾期未付款，已由系統自動取消。';
      break;
    case 'Staff':
      main = '這筆訂單已由客服取消。';
      break;
    case 'Customer':
      main = '你已取消這筆訂單。';
      break;
    default:
      main = '這筆訂單已取消。';
  }

  const lines = [main];
  if (paid) lines.push('已付款項會由我們辦理退款。');
  if (order.cancellationSource === 'Staff' || paid) lines.push('如有疑問請聯絡客服。');
  if (order.cancelledAt) lines.push(`取消時間：${formatDateTimeInTaipei(order.cancelledAt)}（台灣時間）`);
  return lines;
}

export function PaymentOverdueNotice() {
  return (
    <div className="flex flex-col gap-[var(--gg-space-2)]">
      <h2 className="font-display text-[length:var(--gg-text-xl)] font-bold text-warning-text">
        {PAYMENT_OVERDUE_TITLE}
      </h2>
      <p className="text-fg-muted">{PAYMENT_OVERDUE_MESSAGE}</p>
    </div>
  );
}

export function CancellationNotice({ order }: { readonly order: CancellationFields }) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-2)]">
      {cancellationExplanation(order).map((line) => (
        <p key={line} className="text-fg-muted">
          {line}
        </p>
      ))}
    </div>
  );
}
