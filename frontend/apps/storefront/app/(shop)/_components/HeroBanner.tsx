import Link from 'next/link';
import type { components } from '@greygray/api-client/storefront';
import { placeholderImage } from '../_lib/placeholderImage';

type S = components['schemas'];

/** 柔粉漸層 banner ＋ 圓形產品（ADR-009 首頁切版第二段）。沒有開團中的旅程時不顯示。 */
export function HeroBanner({ campaign }: { campaign: S['CampaignListItem'] | undefined }) {
  if (!campaign) return null;

  return (
    <Link
      href={`/campaigns/${campaign.id}`}
      className="relative flex items-center gap-[var(--gg-space-5)] overflow-hidden rounded-[var(--gg-radius-xl)] p-[var(--gg-space-6)] shadow-card"
      style={{ background: 'var(--gg-gradient-banner)' }}
    >
      <div className="flex flex-1 flex-col gap-[var(--gg-space-2)]">
        <span className="text-[length:var(--gg-text-sm)] font-bold text-fg">現正開團</span>
        <h2 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">
          {campaign.title}
        </h2>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          {campaign.destination}出發，現在登記還來得及
        </p>
      </div>
      <div className="h-[var(--gg-space-8)] w-[var(--gg-space-8)] shrink-0 overflow-hidden rounded-pill shadow-raised sm:h-32 sm:w-32">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={campaign.coverImageUrl ?? placeholderImage(campaign.destination)}
          alt={campaign.destination}
          className="h-full w-full object-cover"
        />
      </div>
    </Link>
  );
}
