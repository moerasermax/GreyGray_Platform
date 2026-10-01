import { formatMoney } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { StatusPill } from '@greygray/ui/admin';
import { manualRefundStatusLabel, manualRefundStatusTone } from '../_lib/labels';
import { formatRemittedOn, formatTaipeiDateTime } from '../_lib/paymentDetails';

type S = components['schemas'];

export interface ManualRefundSectionProps {
  readonly paymentId: string;
  readonly manualRefund: S['ManualRefund'] | null | undefined;
  readonly canRecord: boolean;
  readonly onRecord: (paymentId: string) => void;
}

export function ManualRefundSection({ paymentId, manualRefund, canRecord, onRecord }: ManualRefundSectionProps) {
  if (!manualRefund) return null;
  const pending = manualRefund.status === 'Pending';

  return (
    <section className="rounded-card border border-border-soft bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold text-fg">人工退款</h3>
          <StatusPill label={manualRefundStatusLabel(manualRefund.status)} tone={manualRefundStatusTone(manualRefund.status)} />
        </div>
        {pending && canRecord ? (
          <button
            type="button"
            onClick={() => onRecord(paymentId)}
            className="rounded-full border border-primary px-3 py-1 text-xs font-semibold text-primary-text hover:bg-primary-subtle"
          >
            登記已匯款
          </button>
        ) : null}
      </div>
      <dl className="mt-3 grid gap-3 sm:grid-cols-3">
        <div><dt className="text-xs text-fg-muted">應退總額</dt><dd className="mt-1 font-mono font-semibold tabular-nums text-fg">{formatMoney(manualRefund.requiredAmount)}</dd></div>
        <div><dt className="text-xs text-fg-muted">已登記</dt><dd className="mt-1 font-mono font-semibold tabular-nums text-fg">{formatMoney(manualRefund.recordedAmount)}</dd></div>
        <div><dt className="text-xs text-fg-muted">尚待匯出</dt><dd className="mt-1 font-mono font-semibold tabular-nums text-fg">{formatMoney(manualRefund.outstandingAmount)}</dd></div>
      </dl>
      {manualRefund.entries.length > 0 ? (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead><tr className="border-b border-border-soft text-left text-xs text-fg-muted"><th className="px-2 py-2">金額</th><th className="px-2 py-2">匯款日</th><th className="px-2 py-2">備註</th><th className="px-2 py-2">登記者</th><th className="px-2 py-2">登記時間</th></tr></thead>
            <tbody>
              {manualRefund.entries.map((entry) => (
                <tr key={entry.id} className="border-b border-border-soft last:border-0">
                  <td className="px-2 py-2 font-mono tabular-nums text-fg">{formatMoney(entry.amount)}</td>
                  <td className="px-2 py-2 font-mono tabular-nums text-fg">{formatRemittedOn(entry.remittedOn)}</td>
                  <td className="px-2 py-2 text-fg-muted">{entry.note || '—'}</td>
                  <td className="px-2 py-2 text-fg">{entry.recordedByName}</td>
                  <td className="px-2 py-2 text-fg-muted">{formatTaipeiDateTime(entry.recordedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : <p className="mt-3 text-sm text-fg-muted">尚無匯款登記。</p>}
      {manualRefund.status === 'Completed' ? (
        <p className="mt-3 text-sm text-fg-muted">目前已全額登記；之後若有新的退款要求，會再出現待人工退款。</p>
      ) : null}
    </section>
  );
}
