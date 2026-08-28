'use client';

import type { components } from '@greygray/api-client/admin';
import { Field, Input, Textarea } from '@greygray/ui/admin';
import { useState } from 'react';
import { apiErrorMessage } from '../_lib/apiError';
import { fromDatetimeLocalValue, toDatetimeLocalValue } from '../_lib/datetime';
import { Button } from './Button';

type S = components['schemas'];

export interface CampaignFormProps {
  readonly initial?: S['AdminCampaignInput'];
  readonly submitLabel: string;
  /** 已發布（非 `Draft`）的團不能再改基本資料——後端會回 422，這裡先把欄位鎖住。 */
  readonly readOnly?: boolean;
  readonly onSubmit: (body: S['AdminCampaignInput']) => Promise<void>;
}

/** 開團層級的欄位（`AdminCampaignInput`）。狀態操作按鈕在 `StatusActions`，開團商品在 `OffersSection`。 */
export function CampaignForm({ initial, submitLabel, readOnly = false, onSubmit }: CampaignFormProps) {
  const [title, setTitle] = useState(initial?.title ?? '');
  const [destination, setDestination] = useState(initial?.destination ?? '');
  const [departAt, setDepartAt] = useState(initial?.departAt ?? '');
  const [returnAt, setReturnAt] = useState(initial?.returnAt ?? '');
  const [closesAt, setClosesAt] = useState(initial ? toDatetimeLocalValue(initial.closesAt) : '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [coverImageUrl, setCoverImageUrl] = useState(initial?.coverImageUrl ?? '');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit() {
    if (!title.trim() || !destination.trim() || !departAt || !returnAt || !closesAt) return;
    setSubmitting(true);
    setError(null);
    try {
      const body: S['AdminCampaignInput'] = {
        title: title.trim(),
        destination: destination.trim(),
        departAt,
        returnAt,
        closesAt: fromDatetimeLocalValue(closesAt),
        description: description.trim() ? description.trim() : null,
        coverImageUrl: coverImageUrl.trim() ? coverImageUrl.trim() : null,
      };
      await onSubmit(body);
    } catch (cause) {
      setError(apiErrorMessage(cause));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex max-w-xl flex-col gap-4 rounded-card border border-border-soft bg-surface p-5 shadow-card">
      {readOnly ? (
        <p className="text-sm text-fg-muted">
          這個團已經發布，基本資料改成唯讀——開團時定死的內容才能讓「逾時視為照買」這條規則成立。
        </p>
      ) : null}

      {error ? (
        <p role="alert" className="text-sm font-medium text-danger">
          {error}
        </p>
      ) : null}

      <Field label="團名" htmlFor="campaign-title" required>
        <Input
          id="campaign-title"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          maxLength={100}
          readOnly={readOnly}
        />
      </Field>

      <Field label="目的地" htmlFor="campaign-destination" required>
        <Input
          id="campaign-destination"
          value={destination}
          onChange={(event) => setDestination(event.target.value)}
          maxLength={50}
          readOnly={readOnly}
        />
      </Field>

      <div className="grid grid-cols-2 gap-4">
        <Field label="出發日" htmlFor="campaign-depart" required>
          <Input
            id="campaign-depart"
            type="date"
            value={departAt}
            onChange={(event) => setDepartAt(event.target.value)}
            disabled={readOnly}
          />
        </Field>
        <Field label="回程日" htmlFor="campaign-return" required>
          <Input
            id="campaign-return"
            type="date"
            value={returnAt}
            onChange={(event) => setReturnAt(event.target.value)}
            disabled={readOnly}
          />
        </Field>
      </div>

      <Field label="截團時間" htmlFor="campaign-closes-at" required hint="到這個時間 Saga Timer 會自動截團">
        <Input
          id="campaign-closes-at"
          type="datetime-local"
          value={closesAt}
          onChange={(event) => setClosesAt(event.target.value)}
          disabled={readOnly}
        />
      </Field>

      <Field label="說明" htmlFor="campaign-description">
        <Textarea
          id="campaign-description"
          rows={3}
          value={description ?? ''}
          onChange={(event) => setDescription(event.target.value)}
          readOnly={readOnly}
        />
      </Field>

      <Field label="封面圖片網址" htmlFor="campaign-cover" hint="選填">
        <Input
          id="campaign-cover"
          value={coverImageUrl ?? ''}
          onChange={(event) => setCoverImageUrl(event.target.value)}
          readOnly={readOnly}
        />
      </Field>

      {readOnly ? null : (
        <div className="flex justify-end">
          <Button
            variant="primary"
            onClick={() => void handleSubmit()}
            disabled={submitting || !title.trim() || !destination.trim() || !departAt || !returnAt || !closesAt}
          >
            {submitting ? '儲存中…' : submitLabel}
          </Button>
        </div>
      )}
    </div>
  );
}
