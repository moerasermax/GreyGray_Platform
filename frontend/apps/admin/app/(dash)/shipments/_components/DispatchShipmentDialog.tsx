'use client';

import { moneyFromMajorInput } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Input } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import type { DispatchShipmentRequest } from '../_lib/api';
import { Button } from './Button';

type S = components['schemas'];

export interface DispatchShipmentDialogProps {
  readonly open: boolean;
  readonly shipment: S['AdminShipment'] | null;
  readonly onClose: () => void;
  readonly onConfirm: (input: DispatchShipmentRequest) => Promise<void>;
}

/**
 * 交運：填物流商的追蹤單號與**成本**。
 *
 * `carrierCost` 是付給物流商的成本，**不是**向客人收的運費——後者是訂單的 `shippingFee`，
 * 這裡不顯示也不會拿來加總，兩個數字語意不同、不能混（鐵則 2 的直接應用）。
 */
export function DispatchShipmentDialog({ open, shipment, onClose, onConfirm }: DispatchShipmentDialogProps) {
  const [trackingNumber, setTrackingNumber] = useState('');
  const [carrierCostMajor, setCarrierCostMajor] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setTrackingNumber('');
    setCarrierCostMajor('');
    setError(null);
  }, [open, shipment?.id]);

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleSubmit() {
    if (!shipment) return;
    const carrierCost = moneyFromMajorInput(carrierCostMajor, 'TWD');
    if (!trackingNumber.trim() || !carrierCost) {
      setError('請填寫追蹤單號與物流成本（新台幣）。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ trackingNumber: trackingNumber.trim(), carrierCost });
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
      title="交運"
      description="填入物流商回的追蹤單號，以及付給物流商的成本（不是向客人收的運費）。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '送出中…' : '確認交運'}
          </Button>
        </>
      }
    >
      {shipment ? (
        <div className="flex flex-col gap-4">
          {error ? (
            <p role="alert" className="text-sm font-medium text-danger">
              {error}
            </p>
          ) : null}

          <Field label="追蹤單號" htmlFor="dispatch-tracking-number" required>
            <Input
              id="dispatch-tracking-number"
              value={trackingNumber}
              onChange={(event) => setTrackingNumber(event.target.value)}
              placeholder="例如 7-11-88293015"
            />
          </Field>

          <Field
            label="物流成本（元）"
            htmlFor="dispatch-carrier-cost"
            required
            hint="付給物流商的成本，不是向客人收的運費——那個數字在訂單頁的「運費」欄位。"
          >
            <Input
              id="dispatch-carrier-cost"
              type="number"
              inputMode="decimal"
              min={0}
              step="any"
              value={carrierCostMajor}
              onChange={(event) => setCarrierCostMajor(event.target.value)}
            />
          </Field>
        </div>
      ) : null}
    </Dialog>
  );
}
