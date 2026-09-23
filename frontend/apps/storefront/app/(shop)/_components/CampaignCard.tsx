import Link from 'next/link';
import { Badge, Card, Countdown, Thumbnail } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { formatDateRange } from '../_lib/campaignDate';
import { campaignStatusLabel } from '../_lib/campaignStatusLabel';

type S = components['schemas'];

/**
 * 沒有封面時的排版：只用契約給的目的地文字與中性線條，明講「封面尚未提供」，不捏造旅遊照片。
 * 高度由內容決定（不是固定比例），長目的地折行也不會溢出；上方留白是給左上角狀態 badge 的位置。
 * 開團卡片與開團詳情頁共用這一份，缺圖才會是同一種設計語彙。
 */
export function CampaignCoverFallback({ destination }: { destination: string }) {
  return (
    <div className="flex h-full w-full flex-col justify-end gap-[var(--gg-space-1)] px-[var(--gg-space-4)] pb-[var(--gg-space-4)] pt-[var(--gg-space-7)]">
      <span aria-hidden className="mb-[var(--gg-space-1)] block w-[var(--gg-space-6)] border-t border-border-soft" />
      <p className="text-[length:var(--gg-text-xs)] text-fg-muted">目的地</p>
      <p className="break-words font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{destination}</p>
      <p className="text-[length:var(--gg-text-xs)] text-fg-muted">封面照片尚未提供</p>
    </div>
  );
}

/** 純展示，沒有互動狀態，留在 server component 就好——`Countdown` 自己是 client component。 */
export function CampaignCard({ campaign }: { campaign: S['CampaignListItem'] }) {
  const hasCover = Boolean(campaign.coverImageUrl);

  return (
    // 整卡是一個連結；圓角跟著卡片，全域 `:focus-visible` 的外框才會貼著卡片形狀。
    <Link href={`/campaigns/${campaign.id}`} className="block h-full rounded-card">
      <Card padding="none" className="flex h-full flex-col overflow-hidden">
        {hasCover ? (
          <div className="relative aspect-[4/3] w-full overflow-hidden bg-surface-sunken sm:aspect-[16/9]">
            <Thumbnail src={campaign.coverImageUrl} alt={campaign.destination} sizes="(max-width: 640px) 100vw, 33vw" />
            <div className="absolute left-[var(--gg-space-2)] top-[var(--gg-space-2)]">
              <Badge variant={campaign.status} label={campaignStatusLabel(campaign.status)} />
            </div>
          </div>
        ) : (
          <div className="relative w-full border-b border-border-soft bg-surface-sunken">
            <CampaignCoverFallback destination={campaign.destination} />
            <div className="absolute left-[var(--gg-space-2)] top-[var(--gg-space-2)]">
              <Badge variant={campaign.status} label={campaignStatusLabel(campaign.status)} />
            </div>
          </div>
        )}
        <div className="flex min-w-0 flex-1 flex-col gap-[var(--gg-space-2)] p-[var(--gg-space-4)]">
          <h3 className="break-words font-display text-[length:var(--gg-text-lg)] font-bold leading-snug text-fg">
            {campaign.title}
          </h3>
          <div className="flex flex-col gap-[var(--gg-space-1)]">
            <p className="break-words text-[length:var(--gg-text-sm)] text-fg">{campaign.destination}</p>
            <p className="break-words text-[length:var(--gg-text-sm)] text-fg-muted">
              {formatDateRange(campaign.departAt, campaign.returnAt)}
            </p>
          </div>
          <div className="mt-auto pt-[var(--gg-space-1)]">
            <Countdown closesAt={campaign.closesAt} isAcceptingOrders={campaign.isAcceptingOrders} />
          </div>
        </div>
      </Card>
    </Link>
  );
}
