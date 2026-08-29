'use client';

import { ApiError } from '@greygray/api-client';
import { listCampaigns } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { EmptyState, ErrorState, StatusPill } from '@greygray/ui/admin';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { campaignStatusLabel, campaignStatusTone } from '../campaigns/_lib/labels';

type S = components['schemas'];

/**
 * 採購清單是截團後由 `CampaignClosed` 產生的（M1b-1）——`Closed`／`TripInProgress`／`Returned`
 * 這三個狀態才進入採購階段。**這裡只是讀後端回的 `status` 欄位分組，不是拿 `closesAt`
 * 跟現在時間比**（鐵則 3：不猜業務規則）。
 */
const PROCUREMENT_PHASE_STATUSES: ReadonlySet<S['CampaignStatus']> = new Set([
  'Closed',
  'TripInProgress',
  'Returned',
]);

export default function ProcurementEntryPage() {
  const [campaigns, setCampaigns] = useState<readonly S['AdminCampaign'][]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listCampaigns(browserApi(), { limit: 100 })
      .then((page) => {
        if (!cancelled) setCampaigns(page.items);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取開團列表失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  const eligible = campaigns.filter((c) => PROCUREMENT_PHASE_STATUSES.has(c.status));
  const others = campaigns.filter((c) => !PROCUREMENT_PHASE_STATUSES.has(c.status));

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-semibold text-fg">現場採購</h1>
        <p className="mt-1 text-sm text-fg-muted">
          選一個已截團的團，看該團的採購清單。系統不下單給任何人——這裡記錄的是你人站在店裡做了什麼決定。
        </p>
      </div>

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
      ) : eligible.length === 0 ? (
        <EmptyState
          title="目前沒有已截團的團可以採購"
          description="團截團之後才會產生採購清單，回到開團管理確認團的狀態。"
        />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {eligible.map((campaign) => (
            <Link
              key={campaign.id}
              href={`/procurement/${campaign.id}`}
              className="flex min-h-11 flex-col gap-2 rounded-card border border-border-soft bg-surface p-4 shadow-card hover:border-primary"
            >
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium text-fg">{campaign.title}</span>
                <StatusPill label={campaignStatusLabel(campaign.status)} tone={campaignStatusTone(campaign.status)} />
              </div>
              <span className="text-sm text-fg-muted">{campaign.destination}</span>
            </Link>
          ))}
        </div>
      )}

      {!loading && !error && others.length > 0 ? (
        <details className="rounded-card border border-border-soft bg-surface-sunken p-4">
          <summary className="cursor-pointer text-sm font-medium text-fg-muted">
            其他還沒截團的團（{others.length}）
          </summary>
          <div className="mt-3 flex flex-col gap-2">
            {others.map((campaign) => (
              <div key={campaign.id} className="flex items-center justify-between gap-2 text-sm">
                <span className="text-fg">{campaign.title}</span>
                <StatusPill label={campaignStatusLabel(campaign.status)} tone={campaignStatusTone(campaign.status)} />
              </div>
            ))}
          </div>
        </details>
      ) : null}
    </div>
  );
}
