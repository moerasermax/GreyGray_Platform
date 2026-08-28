'use client';

import { cancelCampaign, closeCampaign, publishCampaign, settleCampaign } from '@greygray/api-client/endpoints/admin';
import type { components } from '@greygray/api-client/admin';
import { Dialog, Textarea, useToast } from '@greygray/ui/admin';
import { useState } from 'react';
import { browserApi } from '../../../_lib/apiClient';
import { usePayloadIdempotency } from '../../../_lib/usePayloadIdempotency';
import { getSession, hasRequiredRole } from '../../../login/_lib/session';
import { apiErrorMessage, apiErrorTraceId } from '../_lib/apiError';
import { Button } from './Button';

type S = components['schemas'];

export interface StatusActionsProps {
  readonly campaign: S['AdminCampaign'];
  /** 任何一個操作成功後呼叫——重新 GET 拿最新狀態，不要用 request 內容自己拼（docs/05 §9）。 */
  readonly onChanged: () => void;
}

/**
 * publish／close／cancel／settle 各有前置條件，**不在前端判斷業務規則**——
 * 按鈕能不能按只看 `campaign.status` 這個列舉本身的終止性（例如已取消／已結團就不能再操作），
 * 真正「現在能不能做」由後端的 422 決定，訊息原樣顯示。
 */
export function StatusActions({ campaign, onChanged }: StatusActionsProps) {
  const toast = useToast();
  const idempotency = usePayloadIdempotency();
  const role = getSession()?.role ?? 'ReadOnly';
  const [pending, setPending] = useState<'publish' | 'close' | 'cancel' | 'settle' | null>(null);
  const [actionError, setActionError] = useState<{ message: string; traceId: string | null } | null>(null);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState('');

  const isTerminal = campaign.status === 'Cancelled' || campaign.status === 'Settled';

  async function run(kind: 'publish' | 'close' | 'settle') {
    setPending(kind);
    setActionError(null);
    try {
      const client = browserApi();
      const payload = { campaignId: campaign.id, kind };
      const options = { idempotencyKey: idempotency.current(payload) };
      if (kind === 'publish') await publishCampaign(client, campaign.id, options);
      if (kind === 'close') await closeCampaign(client, campaign.id, options);
      if (kind === 'settle') await settleCampaign(client, campaign.id, options);
      idempotency.complete();
      toast.show('success', '操作成功。');
      onChanged();
    } catch (cause) {
      setActionError({ message: apiErrorMessage(cause), traceId: apiErrorTraceId(cause) });
    } finally {
      setPending(null);
    }
  }

  async function handleCancelConfirm() {
    setPending('cancel');
    setActionError(null);
    try {
      const body = { reason: cancelReason.trim() || '未填寫原因' };
      const payload = { campaignId: campaign.id, kind: 'cancel', body };
      await cancelCampaign(browserApi(), campaign.id, body, { idempotencyKey: idempotency.current(payload) });
      idempotency.complete();
      toast.show('success', '開團已取消。');
      setCancelOpen(false);
      setCancelReason('');
      onChanged();
    } catch (cause) {
      setActionError({ message: apiErrorMessage(cause), traceId: apiErrorTraceId(cause) });
    } finally {
      setPending(null);
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        {hasRequiredRole(role, 'Operator') ? (
          <>
            <Button
              variant="primary"
              onClick={() => void run('publish')}
              disabled={campaign.status !== 'Draft' || pending !== null}
            >
              {pending === 'publish' ? '發布中…' : '發布'}
            </Button>
            <Button
              variant="secondary"
              onClick={() => void run('close')}
              disabled={campaign.status !== 'Open' || pending !== null}
            >
              {pending === 'close' ? '截團中…' : '提前截團'}
            </Button>
          </>
        ) : null}
        {hasRequiredRole(role, 'Accountant') ? (
          <Button
            variant="secondary"
            onClick={() => void run('settle')}
            disabled={campaign.status === 'Draft' || isTerminal || pending !== null}
          >
            {pending === 'settle' ? '結團中…' : '結團'}
          </Button>
        ) : null}
        {hasRequiredRole(role, 'Owner') ? (
          <Button variant="danger" onClick={() => setCancelOpen(true)} disabled={isTerminal || pending !== null}>
            取消開團
          </Button>
        ) : null}
      </div>

      {actionError ? (
        <div role="alert" className="rounded-card border border-danger/30 bg-danger-subtle p-3 text-sm text-danger">
          <p className="font-medium">{actionError.message}</p>
          {actionError.traceId ? <p className="mt-1 text-xs">追蹤碼 {actionError.traceId}</p> : null}
        </div>
      ) : null}

      <Dialog
        open={cancelOpen}
        onClose={() => setCancelOpen(false)}
        title="確定要取消這個團嗎？"
        description="該團全部訂單會取消退款，但已登錄的旅程成本仍然留在帳上。"
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelOpen(false)} disabled={pending !== null}>
              我再想想
            </Button>
            <Button variant="danger" onClick={() => void handleCancelConfirm()} disabled={pending !== null}>
              {pending === 'cancel' ? '取消中…' : '確定取消'}
            </Button>
          </>
        }
      >
        <label htmlFor="cancel-reason" className="text-sm font-medium text-fg">
          取消原因
        </label>
        <Textarea
          id="cancel-reason"
          className="mt-1.5 w-full"
          rows={3}
          value={cancelReason}
          onChange={(event) => setCancelReason(event.target.value)}
          placeholder="例如：航班取消、當地供應商斷貨"
        />
      </Dialog>
    </div>
  );
}
