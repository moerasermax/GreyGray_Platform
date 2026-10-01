import type { components } from '@greygray/api-client/admin';
import { paymentInstructionRows } from '../_lib/paymentDetails';

type S = components['schemas'];

export interface PaymentInstructionsProps {
  readonly instructions: S['PaymentInstructions'] | null | undefined;
  readonly paymentStatus: S['PaymentStatus'];
  readonly orderStatus: S['OrderStatus'];
}

export function PaymentInstructions({ instructions, paymentStatus, orderStatus }: PaymentInstructionsProps) {
  const rows = paymentInstructionRows(instructions);
  if (!instructions || rows.length === 0) return null;
  const invalid = orderStatus === 'Cancelled' && paymentStatus === 'InstructionsIssued';
  const paid = paymentStatus === 'Captured';

  return (
    <div className="rounded-card border border-border-soft bg-surface-sunken p-3 text-sm text-fg">
      <div className="flex flex-wrap items-center gap-2">
        <p className="font-semibold">取號資訊</p>
        {paid ? <span className="rounded-full bg-success-subtle px-2 py-0.5 text-xs font-semibold text-success">已繳費</span> : null}
        {invalid ? <span className="rounded-full bg-danger-subtle px-2 py-0.5 text-xs font-semibold text-danger">已失效，請勿提供給客人</span> : null}
      </div>
      <dl className="mt-2 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {rows.map((row) => (
          <div key={`${row.label}:${row.value}`}>
            <dt className="text-xs text-fg-muted">{row.label}</dt>
            <dd className="mt-0.5 select-all font-mono text-sm tabular-nums text-fg">{row.value}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
