'use client';

import { moneyFromMajorInput } from '@greygray/api-client';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Field, Select } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import type { ReportPriceChangedRequest } from '../_lib/api';
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

export interface ReportPriceChangeDialogProps {
  readonly open: boolean;
  readonly item: S['PurchaseItem'] | null;
  readonly onClose: () => void;
  readonly onConfirm: (input: ReportPriceChangedRequest) => Promise<void>;
}

/**
 * 回報現場漲價。**送出就放行，不等客人回覆**——系統發 LINE 問客人並記錄軌跡，
 * 逾時視為照買，差額由賣方吸收（團價已定死）。不要把這個對話框做成「等待中」的樣子，
 * 送出後立刻可以去買下一項。
 */
export function ReportPriceChangeDialog({ open, item, onClose, onConfirm }: ReportPriceChangeDialogProps) {
  const [currency, setCurrency] = useState<S['Money']['currency']>('JPY');
  const [major, setMajor] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    setCurrency(item?.targetPrice?.currency ?? 'JPY');
    setMajor('');
    setError(null);
  }, [open, item?.id]);

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleSubmit() {
    if (!item) return;
    const newPrice = moneyFromMajorInput(major, currency);
    if (!newPrice) {
      setError('請填寫現場的新價格。');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await onConfirm({ newPrice });
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
      title={`回報漲價：${item?.name ?? ''}`}
      description="送出後系統會立刻通知客人並開始計時，你不必等回覆——逾時沒回覆就視為照買，差額由賣方吸收。"
      footer={
        <>
          <Button variant="secondary" onClick={handleClose} disabled={submitting}>
            取消
          </Button>
          <Button variant="primary" onClick={() => void handleSubmit()} disabled={submitting}>
            {submitting ? '送出中…' : '通知客人並記錄'}
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

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <Field label="幣別" htmlFor="price-changed-currency" required>
              <Select
                id="price-changed-currency"
                value={currency}
                onChange={(event) => setCurrency(event.target.value as S['Money']['currency'])}
                options={CURRENCY_OPTIONS.map((option) => ({ value: option.value, label: option.label }))}
              />
            </Field>
            <Field label="現場新價格" htmlFor="price-changed-amount" required hint="現場標價或收銀機顯示的金額">
              <input
                id="price-changed-amount"
                inputMode="decimal"
                type="number"
                min={0}
                step="any"
                value={major}
                onChange={(event) => setMajor(event.target.value)}
                className="min-h-11 w-full rounded-sm border border-border-strong bg-surface px-3 py-1.5 text-base text-fg focus-visible:outline-2 focus-visible:outline-primary-strong"
              />
            </Field>
          </div>
        </div>
      ) : null}
    </Dialog>
  );
}
