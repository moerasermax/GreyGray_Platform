'use client';

import { ApiError } from '@greygray/api-client';
import { DataTable, ErrorState, Field, FilterBar, Select, StatusPill, type DataTableColumn } from '@greygray/ui/admin';
import { useEffect, useState } from 'react';
import { browserApi } from '../../_lib/apiClient';
import { getSession, hasRequiredRole } from '../../login/_lib/session';
import { TicketDetailDialog } from './_components/TicketDetailDialog';
import {
  getSupportTicket,
  listSupportTickets,
  resolveSupportTicket,
  type SupportTicket,
  type SupportTicketStatus,
} from './_lib/api';
import { ticketContactText, ticketMessageExcerpt, ticketStatusLabel, ticketStatusTone } from './_lib/labels';

const STATUS_OPTIONS: readonly SupportTicketStatus[] = ['open', 'resolved'];

function formatCreatedAt(value: string): string {
  return new Intl.DateTimeFormat('zh-TW', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
}

export default function TicketsPage() {
  // 契約沒有「總數」，未結案的量不大時直接預設只看未結案（§1.2「至少要能只看未結案的」）。
  const [status, setStatus] = useState<SupportTicketStatus | ''>('open');

  const [rows, setRows] = useState<readonly SupportTicket[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<ApiError | Error | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const [selectedTicket, setSelectedTicket] = useState<SupportTicket | null>(null);
  // 契約 `resolve` 要 `Operator` 以上；這一頁本身 `ReadOnly` 就進得來，能不能按「標記已處理」再判一次。
  const canResolve = (() => {
    const session = getSession();
    return session !== null && hasRequiredRole(session.role, 'Operator');
  })();

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    void listSupportTickets(browserApi(), { limit: 20, ...(status ? { status } : {}) })
      .then((page) => {
        if (cancelled) return;
        setRows(page.items);
        setNextCursor(page.nextCursor);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause : new Error('讀取客服留言失敗。'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [status, reloadKey]);

  async function handleLoadMore() {
    if (!nextCursor) return;
    setLoadingMore(true);
    try {
      const page = await listSupportTickets(browserApi(), {
        limit: 20,
        cursor: nextCursor,
        ...(status ? { status } : {}),
      });
      setRows((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error('讀取下一頁失敗。'));
    } finally {
      setLoadingMore(false);
    }
  }

  /** 409 之後：這張單的最新狀態要重讀一次，其餘列不動（不用整頁重打）。 */
  async function refreshOne(id: string) {
    try {
      const fresh = await getSupportTicket(browserApi(), id);
      setRows((current) => current.map((row) => (row.id === id ? fresh : row)));
    } catch {
      // 重讀失敗就交給下一次篩選／換頁自然更新，不因此擋住畫面。
    }
  }

  async function handleResolve(input: { staffNote: string }) {
    if (!selectedTicket) return;
    const resolved = await resolveSupportTicket(browserApi(), selectedTicket.id, {
      staffNote: input.staffNote || null,
    });
    setRows((current) => current.map((row) => (row.id === resolved.id ? resolved : row)));
  }

  const columns: DataTableColumn<SupportTicket>[] = [
    {
      key: 'createdAt',
      header: '什麼時候來的',
      renderCell: (row) => <td className="px-3 py-2 text-fg-muted">{formatCreatedAt(row.createdAt)}</td>,
    },
    {
      key: 'message',
      header: '客人問什麼',
      renderCell: (row) => <td className="px-3 py-2 text-fg">{ticketMessageExcerpt(row.message)}</td>,
    },
    {
      key: 'contact',
      header: '怎麼聯絡',
      renderCell: (row) => <td className="px-3 py-2 text-fg">{ticketContactText(row)}</td>,
    },
    {
      key: 'status',
      header: '處理了沒',
      renderCell: (row) => (
        <td className="px-3 py-2">
          <StatusPill label={ticketStatusLabel(row.status)} tone={ticketStatusTone(row.status)} />
        </td>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold text-fg">客服訊息</h1>

      <FilterBar>
        <Field label="狀態" htmlFor="tickets-filter-status">
          <Select
            id="tickets-filter-status"
            placeholder="全部（含已結案）"
            value={status}
            onChange={(event) => setStatus(event.target.value as SupportTicketStatus | '')}
            options={STATUS_OPTIONS.map((value) => ({ value, label: ticketStatusLabel(value) }))}
          />
        </Field>
      </FilterBar>

      {error ? (
        <ErrorState
          title={error instanceof ApiError ? error.problem.title : error.message}
          traceId={error instanceof ApiError ? error.shortTraceId : null}
          onRetry={() => {
            setError(null);
            setReloadKey((current) => current + 1);
          }}
        />
      ) : (
        <>
          <DataTable
            columns={columns}
            rows={rows}
            getRowKey={(row) => row.id}
            loading={loading}
            onRowClick={(row) => setSelectedTicket(row)}
            emptyTitle="目前沒有客服留言"
            {...(status === 'open' ? { emptyDescription: '沒有未結案的留言。' } : {})}
          />
          {nextCursor ? (
            <div className="flex justify-center">
              <button
                type="button"
                onClick={() => void handleLoadMore()}
                disabled={loadingMore}
                className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken disabled:opacity-60"
              >
                {loadingMore ? '載入中…' : '載入更多'}
              </button>
            </div>
          ) : null}
        </>
      )}

      <TicketDetailDialog
        open={selectedTicket !== null}
        ticket={selectedTicket}
        onClose={() => setSelectedTicket(null)}
        onResolve={handleResolve}
        onConflict={() => {
          if (selectedTicket) void refreshOne(selectedTicket.id);
        }}
        canResolve={canResolve}
      />
    </div>
  );
}
