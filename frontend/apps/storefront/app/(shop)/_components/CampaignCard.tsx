import Link from 'next/link';
import { Badge, Card, Countdown } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { formatDateRange } from '../_lib/campaignDate';
import { campaignStatusLabel } from '../_lib/campaignStatusLabel';
import { placeholderImage } from '../_lib/placeholderImage';

type S = components['schemas'];

/** 純展示，沒有互動狀態，留在 server component 就好——`Countdown` 自己是 client component。 */
export function CampaignCard({ campaign }: { campaign: S['CampaignListItem'] }) {
  return (
    <Link href={`/campaigns/${campaign.id}`} className="block">
      <Card padding="none" className="flex flex-col overflow-hidden">
        <div className="relative aspect-square w-full overflow-hidden bg-surface-sunken sm:aspect-[16/9]">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src={campaign.coverImageUrl ?? placeholderImage(campaign.destination)}
            alt={campaign.destination}
            className="h-full w-full object-cover"
            loading="lazy"
          />
          <div className="absolute left-[var(--gg-space-2)] top-[var(--gg-space-2)]">
            <Badge variant={campaign.status} label={campaignStatusLabel(campaign.status)} />
          </div>
        </div>
        <div className="flex flex-col gap-[var(--gg-space-2)] p-[var(--gg-space-4)]">
          <h3 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">
            {campaign.title}
          </h3>
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
            {campaign.destination}．{formatDateRange(campaign.departAt, campaign.returnAt)}
          </p>
          <Countdown closesAt={campaign.closesAt} isAcceptingOrders={campaign.isAcceptingOrders} />
        </div>
      </Card>
    </Link>
  );
}
