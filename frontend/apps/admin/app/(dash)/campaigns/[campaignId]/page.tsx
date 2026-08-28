'use client';

import { ApiError } from '@greygray/api-client';
import { getCampaign, updateCampaign } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { ErrorState, StatusPill } from '@greygray/ui/admin';
import { useParams } from 'next/navigation';
import { useEffect, useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { CampaignForm } from '../_components/CampaignForm';
import { OffersSection } from '../_components/OffersSection';
import { StatusActions } from '../_components/StatusActions';
import { campaignStatusLabel, campaignStatusTone } from '../_lib/labels';

type S = components['schemas'];

export default function CampaignDetailPage() {
  const params = useParams<{ campaignId: string }>();
  const campaignId = params.campaignId;
  const idempotency = usePayloadIdempotency();

  const [campaign, setCampaign] = useState<S['AdminCampaignDetail'] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void getCampaign(browserApi(), campaignId)
      .then((found) => {
        if (!cancelled) setCampaign(found);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取開團失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [campaignId, reloadKey]);

  async function handleSubmit(body: S['AdminCampaignInput']) {
    const payload = { campaignId, body };
    await updateCampaign(browserApi(), campaignId, body, { idempotencyKey: idempotency.current(payload) });
    idempotency.complete();
    // 寫入完成後重新 GET，不要拿 request 內容當成新狀態（docs/05 §9）。
    setReloadKey((current) => current + 1);
  }

  if (error) {
    return (
      <ErrorState
        title={error instanceof ApiError ? error.problem.title : error.message}
        traceId={error instanceof ApiError ? error.shortTraceId : null}
        onRetry={() => {
          setError(null);
          setReloadKey((current) => current + 1);
        }}
      />
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <h1 className="text-xl font-semibold text-fg">{loading ? '載入中…' : campaign?.title}</h1>
        {campaign ? <StatusPill label={campaignStatusLabel(campaign.status)} tone={campaignStatusTone(campaign.status)} /> : null}
      </div>

      {campaign ? <StatusActions campaign={campaign} onChanged={() => setReloadKey((current) => current + 1)} /> : null}

      {campaign ? (
        <CampaignForm
          key={reloadKey}
          initial={campaign}
          submitLabel="儲存開團資料"
          readOnly={campaign.status !== 'Draft'}
          onSubmit={handleSubmit}
        />
      ) : null}

      {campaign ? (
        <OffersSection
          campaignId={campaignId}
          offers={campaign.offers}
          loading={loading}
          onChanged={() => setReloadKey((current) => current + 1)}
        />
      ) : null}
    </div>
  );
}
