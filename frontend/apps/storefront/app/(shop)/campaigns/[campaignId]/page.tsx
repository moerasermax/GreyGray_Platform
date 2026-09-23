import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { Badge, Countdown, Thumbnail } from '@greygray/ui';
import { getCampaign } from '@greygray/api-client/endpoints/storefront';
import { ApiError } from '@greygray/api-client';
import { serverApi } from '../../../_lib/apiClient';
import { formatDateRange } from '../../_lib/campaignDate';
import { campaignStatusLabel } from '../../_lib/campaignStatusLabel';
import { CampaignCoverFallback } from '../../_components/CampaignCard';
import { CampaignOfferRow } from './_components/CampaignOfferRow';

interface CampaignDetailPageProps {
  params: Promise<{ campaignId: string }>;
}

async function loadCampaign(campaignId: string) {
  const api = await serverApi();
  try {
    return await getCampaign(api, campaignId);
  } catch (cause) {
    if (cause instanceof ApiError && cause.status === 404) notFound();
    throw cause;
  }
}

export async function generateMetadata({ params }: CampaignDetailPageProps): Promise<Metadata> {
  const { campaignId } = await params;
  const campaign = await loadCampaign(campaignId);
  const description = campaign.description ?? `${campaign.destination}採購團，出發前開放登記需求。`;
  return {
    title: campaign.title,
    description,
    openGraph: {
      title: campaign.title,
      description,
      images: campaign.coverImageUrl ? [campaign.coverImageUrl] : undefined,
    },
  };
}

/** 開團詳情走 SSR：LINE 分享的 OG 標題／描述靠這一頁的 `generateMetadata`。 */
export default async function CampaignDetailPage({ params }: CampaignDetailPageProps) {
  const { campaignId } = await params;
  const campaign = await loadCampaign(campaignId);
  const hasCover = Boolean(campaign.coverImageUrl);

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)]">
      {/* 桌面封面與資訊雙欄，封面不再獨佔首屏；手機維持上下排。缺圖時高度由內容決定，不留大塊空白。 */}
      <div className="grid gap-[var(--gg-space-5)] md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)] md:items-start">
        {hasCover ? (
          <div className="relative aspect-[16/9] w-full overflow-hidden rounded-[var(--gg-radius-xl)] bg-surface-sunken">
            <Thumbnail src={campaign.coverImageUrl} alt={campaign.destination} sizes="(max-width: 768px) 100vw, 600px" />
            <div className="absolute left-[var(--gg-space-3)] top-[var(--gg-space-3)]">
              <Badge variant={campaign.status} label={campaignStatusLabel(campaign.status)} />
            </div>
          </div>
        ) : (
          <div className="relative w-full overflow-hidden rounded-[var(--gg-radius-xl)] border border-border-soft bg-surface-sunken">
            <CampaignCoverFallback destination={campaign.destination} />
            <div className="absolute left-[var(--gg-space-3)] top-[var(--gg-space-3)]">
              <Badge variant={campaign.status} label={campaignStatusLabel(campaign.status)} />
            </div>
          </div>
        )}

        <header className="flex min-w-0 flex-col gap-[var(--gg-space-3)]">
          <h1 className="break-words font-display text-[length:var(--gg-text-2xl)] font-extrabold leading-tight text-fg">
            {campaign.title}
          </h1>
          <dl className="flex flex-col gap-[var(--gg-space-2)] border-y border-border-soft py-[var(--gg-space-3)] text-[length:var(--gg-text-sm)]">
            <div className="flex gap-[var(--gg-space-3)]">
              <dt className="shrink-0 text-fg-muted">目的地</dt>
              <dd className="min-w-0 break-words text-fg">{campaign.destination}</dd>
            </div>
            <div className="flex gap-[var(--gg-space-3)]">
              <dt className="shrink-0 text-fg-muted">出發／返回</dt>
              <dd className="min-w-0 break-words text-fg">{formatDateRange(campaign.departAt, campaign.returnAt)}</dd>
            </div>
          </dl>
          <Countdown closesAt={campaign.closesAt} isAcceptingOrders={campaign.isAcceptingOrders} />
          {!campaign.isAcceptingOrders && (
            <p className="rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface px-[var(--gg-space-3)] py-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] text-danger">
              這個團已經截止收單，商品可以看但無法加入購物車。
            </p>
          )}
        </header>
      </div>

      {campaign.description && (
        <p className="whitespace-pre-line break-words text-[length:var(--gg-text-base)] text-fg">{campaign.description}</p>
      )}

      <section className="flex flex-col gap-[var(--gg-space-3)] border-t border-border-soft pt-[var(--gg-space-5)]">
        <div className="flex flex-wrap items-baseline justify-between gap-[var(--gg-space-2)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">團購品項</h2>
          {campaign.offers.length > 0 && (
            <span className="text-[length:var(--gg-text-sm)] text-fg-muted">共 {campaign.offers.length} 項</span>
          )}
        </div>
        {campaign.offers.length === 0 ? (
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">這個團目前還沒有上架品項。</p>
        ) : (
          <div className="grid gap-[var(--gg-space-3)] lg:grid-cols-2">
            {campaign.offers.map((offer) => (
              <CampaignOfferRow key={offer.id} offer={offer} isAcceptingOrders={campaign.isAcceptingOrders} />
            ))}
          </div>
        )}
      </section>
    </main>
  );
}
