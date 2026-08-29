'use client';

import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Textarea } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import type { MarkUnavailableRequest } from '../_lib/api';
import { Button } from './Button';

type S = components['schemas'];

export interface MarkUnavailableDialogProps {
  readonly open: boolean;
  readonly item: S['PurchaseItem'] | null;
  readonly onClose: () => void;
  readonly onConfirm: (input: MarkUnavailableRequest) => Promise<void>;
}

/**
 * 標記現場缺貨。這個品項對應的 OrderLine 會直接取消並退款，
 * 訂單其餘品項照常出貨——**退款去向由客人選**，這裡只負責記錄缺貨事實與原因（ADR-023）。
 */
export function MarkUnavailableDialog({ open, item, onClose, onConfirm }: MarkUnavailableDialogProps) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setReason('');
    setError(null);
  }, [open, item?.id]);

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleSubmit() {
    if (!item) return;
    if (!reason.trim()) {
      setError('請填寫缺貨原因。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ reason: reason.trim() });
      onClose();
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open && item !== null}
      onClose={handleClose}
      title={`標記缺貨：${item?.name ?? ''}`}
      description="這個品項對應的訂單品項會取消並退款，訂單其餘品項照常出貨。退款去向由客人選，不是在這裡決定。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="danger" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '送出中…' : '確認缺貨'}
          </Button>
        </>
      }
    >
      {item ? (
        <div className="flex flex-col gap-4">
          <Field label="缺貨原因" htmlFor="unavailable-reason" error={error} required>
            <Textarea
              id="unavailable-reason"
              rows={3}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="例如：現場架上已經賣完，問了店員也沒有補貨"
            />
          </Field>
        </div>
      ) : null}
    </Dialog>
  );
}
