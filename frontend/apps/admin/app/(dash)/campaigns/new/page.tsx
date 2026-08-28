'use client';

import { createCampaign } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { useRouter } from 'next/navigation';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { CampaignForm } from '../_components/CampaignForm';

type S = components['schemas'];

export default function NewCampaignPage() {
  const router = useRouter();
  const idempotency = usePayloadIdempotency();

  async function handleSubmit(body: S['AdminCampaignInput']) {
    const created = await createCampaign(browserApi(), body, { idempotencyKey: idempotency.current(body) });
    idempotency.complete();
    router.push(`/campaigns/${created.id}`);
  }

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-fg">新增開團</h1>
      <p className="max-w-xl text-sm text-fg-muted">
        建立後狀態是「草稿」，可以先加開團商品，確認沒問題再發布。
      </p>
      <CampaignForm submitLabel="建立開團（草稿）" onSubmit={handleSubmit} />
    </div>
  );
}
