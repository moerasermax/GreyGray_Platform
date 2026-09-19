'use client';

import { ApiError } from '@greygray/api-client';
import { Dialog, Field, StatusPill, Textarea } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import type { SupportTicket } from '../_lib/api';
import { ticketContactText, ticketMenuPathText, ticketStatusLabel, ticketStatusTone } from '../_lib/labels';

export interface TicketDetailDialogProps {
  readonly open: boolean;
  readonly ticket: SupportTicket | null;
  readonly onClose: () => void;
  readonly onResolve: (input: { staffNote: string }) => Promise<void>;
  /**
   * 409 `support.already-resolved`：別人剛處理過。不擋住畫面，交給呼叫端
   * 重新讀這張單、把列表同步成最新狀態——`docs/37` §1.2 明講「要刷新，不要卡住」。
   */
  readonly onConflict: () => void;
  /** 契約 §0.3：`resolve` 要 `Operator` 以上。`ReadOnly` 只能看，看不到「標記已處理」。 */
  readonly canResolve: boolean;
}

/** 客服留言的詳情＋「標記已處理」。列表已經是完整的 `SupportTicket`，不用再打一次 GET（避免 N+1）。 */
export function TicketDetailDialog({
  open,
  ticket,
  onClose,
  onResolve,
  onConflict,
  canResolve,
}: TicketDetailDialogProps) {
  const [staffNote, setStaffNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setStaffNote('');
      setError(null);
    }
  }, [open, ticket?.id]);

  function handleClose() {
    if (submitting) return;
    onClose();
  }

  async function handleResolve() {
    setSubmitting(true);
    setError(null);
    try {
      await onResolve({ staffNote: staffNote.trim() });
      onClose();
    } catch (cause) {
      if (cause instanceof ApiError && cause.is('support.already-resolved')) {
        setError('這張留言剛剛已經被別人標記完成，畫面已經重新整理。');
        onConflict();
        return;
      }
      setError(
        cause instanceof ApiError
          ? cause.problem.detail
            ? `${cause.problem.title}（${cause.problem.detail}）`
            : cause.problem.title
          : '標記失敗，請稍後再試。',
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (!ticket) return null;

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      title="客服留言"
      footer={
        ticket.status === 'open' && canResolve ? (
          <>
            <button
              type="button"
              onClick={handleClose}
              disabled={submitting}
              className="rounded-sm border border-border-strong px-3 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
            >
              關閉
            </button>
            <button
              type="button"
              onClick={() => void handleResolve()}
              disabled={submitting}
              className="rounded-sm bg-primary px-3 py-1.5 text-sm font-semibold text-on-primary hover:opacity-90 disabled:opacity-60"
            >
              {submitting ? '處理中…' : '標記已處理'}
            </button>
          </>
        ) : (
          <button
            type="button"
            onClick={handleClose}
            className="rounded-sm border border-border-strong px-3 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken"
          >
            關閉
          </button>
        )
      }
    >
      <div className="flex flex-col gap-3">
        <StatusPill label={ticketStatusLabel(ticket.status)} tone={ticketStatusTone(ticket.status)} />

        <div>
          <p className="text-xs font-medium text-fg-muted">留言內容</p>
          <p className="whitespace-pre-wrap text-sm text-fg">{ticket.message}</p>
        </div>

        <div>
          <p className="text-xs font-medium text-fg-muted">聯絡方式</p>
          <p className="text-sm text-fg">{ticketContactText(ticket)}</p>
        </div>

        <div>
          <p className="text-xs font-medium text-fg-muted">客人是卡在哪一題才問的</p>
          <p className="text-sm text-fg">{ticketMenuPathText(ticket.menuPath)}</p>
        </div>

        {ticket.orderId ? (
          <div>
            <p className="text-xs font-medium text-fg-muted">相關訂單</p>
            <p className="gg-numeric text-sm text-fg">{ticket.orderId}</p>
          </div>
        ) : null}

        {ticket.status === 'resolved' ? (
          <div>
            <p className="text-xs font-medium text-fg-muted">處理備註</p>
            <p className="text-sm text-fg">{ticket.staffNote ?? '（沒有留備註）'}</p>
          </div>
        ) : canResolve ? (
          <Field label="處理備註" htmlFor="ticket-staff-note" hint="選填，留給下一個看到這張單的人看的">
            <Textarea
              id="ticket-staff-note"
              value={staffNote}
              onChange={(event) => setStaffNote(event.target.value)}
              rows={3}
            />
          </Field>
        ) : null}

        {error ? (
          <p role="alert" className="text-sm font-medium text-danger">
            {error}
          </p>
        ) : null}
      </div>
    </Dialog>
  );
}
