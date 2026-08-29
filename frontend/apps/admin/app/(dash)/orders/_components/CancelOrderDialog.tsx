'use client';

import { ApiError } from '@greygray/api-client';
import { Dialog, Field, Textarea } from '@greygray/ui/admin';
import type { components } from '@greygray/api-client/admin';
import { useState } from 'react';
import { RefundDestinationFields } from './RefundDestinationFields';

type S = components['schemas'];

export interface CancelOrderDialogProps {
  readonly open: boolean;
  readonly orderNumber: string;
  readonly onClose: () => void;
  readonly onConfirm: (input: { reason: string; refundTo: S['RefundDestination'] }) => Promise<void>;
}

/** 整張訂單取消。取消後該單全部品項一併取消退款。 */
export function CancelOrderDialog({ open, orderNumber, onClose, onConfirm }: CancelOrderDialogProps) {
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
      setError('請填寫取消原因。');
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
          : '取消失敗，請稍後再試。',
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title={`取消整張訂單 ${orderNumber}`}
      description="取消後訂單內所有品項會一併取消並退款，這個動作無法復原。"
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
            {submitting ? '處理中…' : '確定取消整張訂單'}
          </button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label="取消原因" htmlFor="cancel-order-reason" error={error} required>
          <Textarea
            id="cancel-order-reason"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="例如：客戶要求取消、商品缺貨無法出貨"
          />
        </Field>
        <RefundDestinationFields name="cancel-order-refund-to" value={refundTo} onChange={setRefundTo} />
      </div>
    </Dialog>
  );
}
