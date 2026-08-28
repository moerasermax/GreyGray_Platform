'use client';

import { ApiError } from '@greygray/api-client';
import { Dialog, Field, Textarea } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';
import { useState } from 'react';
import { RefundDestinationFields } from './RefundDestinationFields';

type S = components['schemas'];

export interface CancelOrderLineDialogProps {
  readonly open: boolean;
  readonly lineName: string;
  readonly onClose: () => void;
  readonly onConfirm: (input: { reason: string; refundTo: S['RefundDestination'] }) => Promise<void>;
}

/** 單一品項取消（例如缺貨），訂單其餘品項照常出貨。 */
export function CancelOrderLineDialog({ open, lineName, onClose, onConfirm }: CancelOrderLineDialogProps) {
  const [reason, setReason] = useState('');
  const [refundTo, setRefundTo] = useState<S['RefundDestination']>('StoredValue');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function reset() {
    setReason('');
    setRefundTo('StoredValue');
    setError(null);
  }

  function handleClose() {
    if (submitting) return;
    reset();
    onClose();
  }

  async function handleSubmit() {
    if (!reason.trim()) {
      setError('請填寫取消原因。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ reason: reason.trim(), refundTo });
      reset();
      onClose();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.problem.title : '取消失敗，請稍後再試。');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title={`取消品項：${lineName}`}
      description="只取消這個品項並退款，訂單其餘品項照常處理與出貨。"
      footer={
        <>
          <button
            type="button"
            onClick={handleClose}
            disabled={submitting}
            className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
          >
            不取消
          </button>
          <button
            type="button"
            onClick={() => void handleSubmit()}
            disabled={submitting}
            className="rounded-full border border-danger/30 bg-danger-subtle px-4 py-1.5 text-sm font-semibold text-danger hover:opacity-90 disabled:opacity-60"
          >
            {submitting ? '處理中…' : '確定取消此品項'}
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label="取消原因" htmlFor="cancel-line-reason" error={error} required>
          <Textarea
            id="cancel-line-reason"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="例如：這個品項當地缺貨買不到"
          />
        </Field>
        <RefundDestinationFields name="cancel-line-refund-to" value={refundTo} onChange={setRefundTo} />
      </div>
    </Dialog>
  );
}
