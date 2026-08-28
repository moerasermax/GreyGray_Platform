import type { Metadata } from 'next';
import { listCampaigns } from '@greygray/api-client/endpoints/storefront';
import { serverApi } from '../../_lib/apiClient';
import { InfiniteCampaignList } from '../_components/InfiniteCampaignList';

export const metadata: Metadata = { title: '開團列表' };

export default async function CampaignsPage() {
  const api = await serverApi();
  const firstPage = await listCampaigns(api, { status: 'Open', limit: 10 });

  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-5)] px-[var(--gg-space-4)] py-[var(--gg-space-4)]">
      <header className="flex flex-col gap-[var(--gg-space-1)]">
        <h1 className="font-display text-[length:var(--gg-text-2xl)] font-extrabold text-fg">開團列表</h1>
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">目前開放登記需求的旅程。</p>
      </header>

      <InfiniteCampaignList
        initialItems={firstPage.items}
        initialCursor={firstPage.nextCursor}
        status="Open"
      />
    </main>
  );
}
