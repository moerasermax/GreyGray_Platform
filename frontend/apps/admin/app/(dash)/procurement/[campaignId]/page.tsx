'use client';

import { ApiError, formatMoney } from '@greygray/api-client';
import { getCampaign } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { EmptyState, ErrorState, Field, Select, StatusPill, useToast } from '@greygray/ui/admin';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import {
  listPurchaseItems,
  markUnavailable,
  reportPriceChanged,
  reportPurchased,
  type MarkUnavailableRequest,
  type ReportPriceChangedRequest,
  type ReportPurchasedRequest,
} from '../_lib/api';
import { purchaseItemStatusLabel, purchaseItemStatusTone } from '../_lib/labels';
import { Button } from '../_components/Button';
import { MarkUnavailableDialog } from '../_components/MarkUnavailableDialog';
import { ReportPriceChangeDialog } from '../_components/ReportPriceChangeDialog';
import { ReportPurchasedDialog } from '../_components/ReportPurchasedDialog';

type S = components['schemas'];

const STATUS_OPTIONS: readonly S['PurchaseItemStatus'][] = [
  'Pending',
  'Purchased',
  'PriceChangedPendingConfirmation',
  'Unavailable',
];

export default function ProcurementCampaignPage() {
  const params = useParams<{ campaignId: string }>();
  const campaignId = params.campaignId;
  const toast = useToast();
  const idempotency = usePayloadIdempotency();

  const [campaignTitle, setCampaignTitle] = useState<string | null>(null);
  const [status, setStatus] = useState('');
  const [items, setItems] = useState<readonly S['PurchaseItem'][]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [reportingItem, setReportingItem] = useState<S['PurchaseItem'] | null>(null);
  const [unavailableItem, setUnavailableItem] = useState<S['PurchaseItem'] | null>(null);
  const [priceChangeItem, setPriceChangeItem] = useState<S['PurchaseItem'] | null>(null);

  useEffect(() => {
    let cancelled = false;
    void getCampaign(browserApi(), campaignId).then(
      (campaign) => {
        if (!cancelled) setCampaignTitle(campaign.title);
      },
      () => {
        // 標題拿不到就用 id 頂替，不擋採購清單本身。
      },
    );
    return () => {
      cancelled = true;
    };
  }, [campaignId]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listPurchaseItems(browserApi(), campaignId, status ? { status: status as S['PurchaseItemStatus'] } : {})
      .then((found) => {
        if (!cancelled) setItems(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取採購清單失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [campaignId, status, reloadKey]);

  async function handleReportConfirm(input: ReportPurchasedRequest) {
    if (!reportingItem) return;
    const payload = { purchaseItemId: reportingItem.id, input };
    await reportPurchased(browserApi(), reportingItem.id, input, {
      idempotencyKey: idempotency.current(payload),
    });
    idempotency.complete();
    toast.show('success', `「${reportingItem.name}」已回報買到。`);
    setReportingItem(null);
    setReloadKey((current) => current + 1);
  }

  async function handleMarkUnavailableConfirm(input: MarkUnavailableRequest) {
    if (!unavailableItem) return;
    const targetId = unavailableItem.id;
    const payload = { purchaseItemId: targetId, input };
    await markUnavailable(browserApi(), targetId, input, {
      idempotencyKey: idempotency.current(payload),
    });
    idempotency.complete();
    toast.show('success', `「${unavailableItem.name}」已標記缺貨並退款。`);
    // 這支端點只回 200，不回完整的 PurchaseItem，狀態變化在本地直接套用。
    setItems((current) =>
      current.map((item) =>
        item.id === targetId ? { ...item, status: 'Unavailable', decidedAt: new Date().toISOString() } : item,
      ),
    );
    setUnavailableItem(null);
  }

  async function handleReportPriceChangeConfirm(input: ReportPriceChangedRequest) {
    if (!priceChangeItem) return;
    const targetId = priceChangeItem.id;
    const originalPrice = priceChangeItem.targetPrice ?? input.newPrice;
    const payload = { purchaseItemId: targetId, input };
    const result = await reportPriceChanged(browserApi(), targetId, input, {
      idempotencyKey: idempotency.current(payload),
    });
    idempotency.complete();
    const deadline = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(
      new Date(result.timeoutAt),
    );
    toast.show('success', `已通知客人。${deadline} 前沒回覆就自動視為照買，你可以繼續買下一項。`);
    // 這支端點只回 { inquiryId, timeoutAt }，詢價軌跡在本地依回應組出來顯示。
    setItems((current) =>
      current.map((item) =>
        item.id === targetId
          ? {
              ...item,
              status: 'PriceChangedPendingConfirmation',
              inquiry: {
                id: result.inquiryId,
                originalPrice,
                newPrice: input.newPrice,
                askedAt: new Date().toISOString(),
                timeoutAt: result.timeoutAt,
                repliedAt: null,
                outcome: null,
                replyText: null,
              },
            }
          : item,
      ),
    );
    setPriceChangeItem(null);
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Link href="/procurement" className="text-sm text-primary hover:underline">
          ← 回選團
        </Link>
        <h1 className="mt-1 text-xl font-semibold text-fg">{campaignTitle ?? campaignId}</h1>
        <p className="mt-1 text-sm text-fg-muted">現場買到後在這裡回報，這一版只接受全數買到。</p>
      </div>

      <Field label="狀態篩選" htmlFor="purchase-items-status">
        <Select
          id="purchase-items-status"
          placeholder="全部狀態"
          value={status}
          onChange={(event) => setStatus(event.target.value)}
          options={STATUS_OPTIONS.map((value) => ({ value, label: purchaseItemStatusLabel(value) }))}
        />
      </Field>

      {error ? (
        <ErrorState
          title={error instanceof ApiError ? error.problem.title : error.message}
          traceId={error instanceof ApiError ? error.shortTraceId : null}
          onRetry={() => {
            setError(null);
            setReloadKey((current) => current + 1);
          }}
        />
      ) : loading ? (
        <div className="h-40 animate-pulse rounded-card bg-surface-sunken" />
      ) : items.length === 0 ? (
        <EmptyState title="沒有符合條件的採購項目" description="換個狀態篩選看看。" />
      ) : (
        <ul className="flex flex-col gap-3">
          {items.map((item) => (
            <li key={item.id} className="rounded-card border border-border-soft bg-surface p-4 shadow-card">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-medium text-fg">{item.name}</p>
                  {item.variantName ? <p className="text-sm text-fg-muted">{item.variantName}</p> : null}
                  {item.orderNumber ? (
                    <p className="gg-numeric mt-1 text-xs text-fg-muted">訂單 {item.orderNumber}</p>
                  ) : null}
                </div>
                <StatusPill label={purchaseItemStatusLabel(item.status)} tone={purchaseItemStatusTone(item.status)} />
              </div>

              <div className="mt-3 grid grid-cols-2 gap-2 text-sm sm:grid-cols-3">
                <div>
                  <p className="text-xs text-fg-muted">需求數量</p>
                  <p className="gg-numeric font-semibold text-fg">{item.quantityRequested}</p>
                </div>
                <div>
                  <p className="text-xs text-fg-muted">已買數量</p>
                  <p className="gg-numeric font-semibold text-fg">{item.quantityPurchased}</p>
                </div>
                <div>
                  <p className="text-xs text-fg-muted">目標採購價</p>
                  <p className="gg-numeric font-semibold text-fg">
                    {item.targetPrice ? formatMoney(item.targetPrice) : '—'}
                  </p>
                </div>
              </div>

              {item.status === 'Pending' ? (
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button variant="primary" onClick={() => setReportingItem(item)}>
                    回報買到
                  </Button>
                  <Button variant="secondary" onClick={() => setPriceChangeItem(item)}>
                    回報漲價
                  </Button>
                  <Button variant="danger" onClick={() => setUnavailableItem(item)}>
                    標記缺貨
                  </Button>
                </div>
              ) : null}

              {item.status === 'PriceChangedPendingConfirmation' && item.inquiry ? (
                <div className="mt-3 rounded-card bg-info-subtle px-3 py-2 text-sm text-fg-on-tint">
                  <p>
                    現場新價 <span className="gg-numeric font-semibold">{formatMoney(item.inquiry.newPrice)}</span>
                    （原價 <span className="gg-numeric">{formatMoney(item.inquiry.originalPrice)}</span>）
                  </p>
                  <p className="mt-1">
                    已通知客人，不必等回覆。
                    {new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(
                      new Date(item.inquiry.timeoutAt),
                    )}{' '}
                    前沒回覆就自動視為照買，差額由賣方吸收。
                  </p>
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}

      <ReportPurchasedDialog
        open={reportingItem !== null}
        item={reportingItem}
        onClose={() => setReportingItem(null)}
        onConfirm={handleReportConfirm}
      />
      <ReportPriceChangeDialog
        open={priceChangeItem !== null}
        item={priceChangeItem}
        onClose={() => setPriceChangeItem(null)}
        onConfirm={handleReportPriceChangeConfirm}
      />
      <MarkUnavailableDialog
        open={unavailableItem !== null}
        item={unavailableItem}
        onClose={() => setUnavailableItem(null)}
        onConfirm={handleMarkUnavailableConfirm}
      />
    </div>
  );
}
