'use client';

import { moneyFromMajorInput } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Select } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import type { ReportPurchasedRequest } from '../_lib/api';
import { Button } from './Button';

type S = components['schemas'];

/** 契約支援的幣別（`money.ts` 的 `Currency`）。現場最常用的幾種排在前面。 */
const CURRENCY_OPTIONS: readonly { value: S['Money']['currency']; label: string }[] = [
  { value: 'JPY', label: '日圓 JPY' },
  { value: 'KRW', label: '韓元 KRW' },
  { value: 'TWD', label: '新台幣 TWD' },
  { value: 'USD', label: '美金 USD' },
  { value: 'HKD', label: '港幣 HKD' },
  { value: 'CNY', label: '人民幣 CNY' },
  { value: 'THB', label: '泰銖 THB' },
  { value: 'EUR', label: '歐元 EUR' },
  { value: 'GBP', label: '英鎊 GBP' },
  { value: 'SGD', label: '新加坡幣 SGD' },
];

export interface ReportPurchasedDialogProps {
  readonly open: boolean;
  readonly item: S['PurchaseItem'] | null;
  readonly onClose: () => void;
  readonly onConfirm: (input: ReportPurchasedRequest) => Promise<void>;
}

/**
 * 回報買到。**M1b-1 只接受全數買到**——數量鎖死等於需求數量，不給改，
 * 送出前就講清楚，不要讓人填了一半才被後端 422 打回。
 *
 * 記帳幣一律 TWD，這是後端的硬性驗證；原幣可以是別的，兩個欄位都要有，
 * 不能只留一個自己換算——匯率不是前端的事（鐵則 2）。
 */
export function ReportPurchasedDialog({ open, item, onClose, onConfirm }: ReportPurchasedDialogProps) {
  const [originalCurrency, setOriginalCurrency] = useState<S['Money']['currency']>('JPY');
  const [originalMajor, setOriginalMajor] = useState('');
  const [bookingMajor, setBookingMajor] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setOriginalCurrency('JPY');
    setOriginalMajor('');
    setBookingMajor('');
    setError(null);
  }, [open, item?.id]);

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleSubmit() {
    if (!item) return;
    const actualPaidOriginal = moneyFromMajorInput(originalMajor, originalCurrency);
    const actualPaidBooking = moneyFromMajorInput(bookingMajor, 'TWD');
    if (!actualPaidOriginal || !actualPaidBooking) {
      setError('請填寫原幣與記帳幣（TWD）的實付金額。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({
        quantityPurchased: item.quantityRequested,
        actualPaidOriginal,
        actualPaidBooking,
      });
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
      title={`回報買到：${item?.name ?? ''}`}
      description="這一版只接受全數買到，數量鎖死等於需求數量。部分買到要等短缺退款的去向定義完才會開放。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '送出中…' : '確認買到'}
          </Button>
        </>
      }
    >
      {item ? (
        <div className="flex flex-col gap-4">
          {error ? (
            <p role="alert" className="text-sm font-medium text-danger">
              {error}
            </p>
          ) : null}

          <div className="rounded-card bg-surface-sunken px-3 py-2 text-sm text-fg">
            回報數量：<span className="gg-numeric font-semibold">{item.quantityRequested}</span>
            {item.variantName ? <span className="ml-2 text-fg-muted">{item.variantName}</span> : null}
          </div>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field label="原幣幣別" htmlFor="purchased-original-currency" required>
              <Select
                id="purchased-original-currency"
                value={originalCurrency}
                onChange={(event) => setOriginalCurrency(event.target.value as S['Money']['currency'])}
                options={CURRENCY_OPTIONS.map((option) => ({ value: option.value, label: option.label }))}
              />
            </Field>
            <Field label="原幣實付金額" htmlFor="purchased-original-amount" required hint="現場刷卡單或收據上的金額">
              <input
                id="purchased-original-amount"
                inputMode="decimal"
                type="number"
                min={0}
                step="any"
                value={originalMajor}
                onChange={(event) => setOriginalMajor(event.target.value)}
                className="min-h-11 w-full rounded-sm border border-border-strong bg-surface px-3 py-1.5 text-base text-fg focus-visible:outline-2 focus-visible:outline-primary"
              />
            </Field>
          </div>

          <Field
            label="記帳幣（TWD）實付金額"
            htmlFor="purchased-booking-amount"
            required
            hint="刷卡帳單上的台幣金額，這個才入帳——本專案不做匯兌損益"
          >
            <input
              id="purchased-booking-amount"
              inputMode="decimal"
              type="number"
              min={0}
              step="any"
              value={bookingMajor}
              onChange={(event) => setBookingMajor(event.target.value)}
              className="min-h-11 w-full rounded-sm border border-border-strong bg-surface px-3 py-1.5 text-base text-fg focus-visible:outline-2 focus-visible:outline-primary"
            />
          </Field>
        </div>
      ) : null}
    </Dialog>
  );
}
