'use client';

import { ApiError } from '@greygray/api-client';
import { Dialog, Field, Textarea } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';
import { useState } from 'react';
import { RefundDestinationFields } from './RefundDestinationFields';

type S = components['schemas'];

export interface RefundShortfallDialogProps {
  readonly open: boolean;
  readonly lineName: string;
  readonly shortfallQuantity: number;
  readonly onClose: () => void;
  readonly onConfirm: (input: { reason: string; refundTo: S['RefundDestination'] }) => Promise<void>;
}

/** 品項部分買到，短缺數量單獨退款，買到的數量照常出貨。 */
export function RefundShortfallDialog({
  open,
  lineName,
  shortfallQuantity,
  onClose,
  onConfirm,
}: RefundShortfallDialogProps) {
  const [reason, setReason] = useState('');
  const [refundTo, setRefundTo] = useState<S['RefundDestination'] | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function reset() {
    setReason('');
    setRefundTo(null);
    setError(null);
  }

  function handleClose() {
    if (submitting) return;
    reset();
    onClose();
  }

  async function handleSubmit() {
    if (!reason.trim()) {
      setError('請填寫退款原因。');
      return;
    }
    if (!refundTo) {
      setError('請選擇退款去處——這是客人的選擇，不能代選。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ reason: reason.trim(), refundTo });
      reset();
      onClose();
    } catch (cause) {
      setError(
        cause instanceof ApiError
          ? cause.problem.detail
            ? `${cause.problem.title}（${cause.problem.detail}）`
            : cause.problem.title
          : '退款失敗，請稍後再試。',
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title={`退還短缺款：${lineName}`}
      description={`買到的數量已經出貨，短缺的 ${shortfallQuantity} 件現在退款。`}
      footer={
        <>
          <button
            type="button"
            onClick={handleClose}
            disabled={submitting}
            className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
          >
            不退款
          </button>
          <button
            type="button"
            onClick={() => void handleSubmit()}
            disabled={submitting}
            className="rounded-full border border-danger/30 bg-danger-subtle px-4 py-1.5 text-sm font-semibold text-danger hover:opacity-90 disabled:opacity-60"
          >
            {submitting ? '處理中…' : '確定退款'}
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label="退款原因" htmlFor="refund-shortfall-reason" error={error} required>
          <Textarea
            id="refund-shortfall-reason"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="例如：這批只買到部分數量，剩下的短缺退款"
          />
        </Field>
        <RefundDestinationFields name="refund-shortfall-refund-to" value={refundTo} onChange={setRefundTo} />
      </div>
    </Dialog>
  );
}
