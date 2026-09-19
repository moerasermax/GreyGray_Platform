/**
 * 客服工單畫面用的純函式：狀態標籤、節錄、聯絡方式顯示。跟 `orders/_lib/labels.ts` 同一個風格——
 * `switch` 一律有 `default`，後端新增列舉值不會讓畫面崩掉。
 */
import type { StatusTone } from '@greygray/ui/admin';
import type { SupportTicket, SupportTicketStatus } from './api';

export function ticketStatusLabel(status: SupportTicketStatus | (string & {})): string {
  switch (status) {
    case 'open':
      return '未結案';
    case 'resolved':
      return '已結案';
    default:
      return status;
  }
}

export function ticketStatusTone(status: SupportTicketStatus | (string & {})): StatusTone {
  switch (status) {
    case 'open':
      return 'warning';
    case 'resolved':
      return 'success';
    default:
      return 'neutral';
  }
}

/** 列表上「客人問什麼」的節錄。太長就截斷，不然一列會被撐爆。 */
export function ticketMessageExcerpt(message: string, maxLength = 40): string {
  const trimmed = message.trim();
  if (trimmed.length <= maxLength) return trimmed;
  return `${trimmed.slice(0, maxLength)}…`;
}

/** 「怎麼聯絡」欄：email 與手機都有就兩個都列，都沒有就是異常資料，仍要講得出「沒有」。 */
export function ticketContactText(ticket: Pick<SupportTicket, 'contactEmail' | 'contactPhone'>): string {
  const parts = [ticket.contactEmail, ticket.contactPhone].filter((value): value is string => Boolean(value));
  return parts.length > 0 ? parts.join('．') : '—';
}

/** 客人是卡在哪一題才問的；`menuPath` 是空陣列代表客人直接跳過選單（理論上契約允許 ❌ 選填）。 */
export function ticketMenuPathText(menuPath: readonly string[]): string {
  return menuPath.length > 0 ? menuPath.join(' › ') : '（沒有走過選單）';
}
