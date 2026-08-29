'use client';

import type { components } from '@greygray/api-client/admin';
import { Dialog } from '@greygray/ui/admin';
import { useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface DeliverConfirmDialogProps {
  readonly open: boolean;
  readonly shipment: S['AdminShipment'] | null;
  readonly onClose: () => void;
  readonly onConfirm: () => Promise<void>;
}

/**
 * 人工補登送達。契約明寫：正常情況由物流商回報自動觸發，這支端點是給
 * 面交自取這種「物流商不會有任何回報」的情境用的（`docs/api/openapi.admin.yaml`
 * `/v1/shipments/{shipmentId}/deliver` description）。
 *
 * 送達不等於訂單完成——訂單要等鑑賞期屆滿才轉 `Completed`，那是 Saga Timer 的事，
 * 這裡不處理也不假裝處理。
 */
export function DeliverConfirmDialog({ open, shipment, onClose, onConfirm }: DeliverConfirmDialogProps) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleClose() {
    if (submitting) return;
    setError(null);
    onClose();
  }

  async function handleConfirm() {
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm();
      onClose();
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Dialog
      open={open && shipment !== null}
      onClose={handleClose}
      title="確定要標記為已送達嗎？"
      description="這是人工補登（例如面交自取），正常情況下物流商回報會自動觸發。訂單要等鑑賞期屆滿才會轉為完成，不是這裡的事。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleConfirm()} disabled={submitting}>
            {submitting ? '處理中…' : '確認已送達'}
          </Button>
        </>
      }
    >
      {error ? (
        <p role="alert" className="text-sm font-medium text-danger">
          {error}
        </p>
      ) : null}
    </Dialog>
  );
}
