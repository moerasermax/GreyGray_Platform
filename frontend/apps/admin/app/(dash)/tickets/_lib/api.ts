/**
 * 客服工單（ADR-040）契約型別與呼叫——不進 `packages/api-client`（FE-34 正在動那個套件），
 * 等這一波收完由 Leader 決定要不要收編。跟 storefront 那份（`app/_lib/supportTickets.ts`）
 * 是同一份契約，但獨立維護：兩個 app 不共用 `_lib`，複製一份型別比硬牽一條跨 app 依賴划算。
 *
 * 契約見 `docs/37-前端第二十二波派工書-第二包.md` §0.3（Leader 已凍結，與後端 BE-55 同一份）。
 */
import type { ApiClient } from '@greygray/api-client';

export type SupportTicketStatus = 'open' | 'resolved';

export interface SupportTicket {
  readonly id: string;
  readonly status: SupportTicketStatus;
  readonly message: string;
  readonly contactEmail: string | null;
  readonly contactPhone: string | null;
  readonly menuPath: readonly string[];
  readonly orderId: string | null;
  readonly customerId: string | null;
  readonly createdAt: string;
  readonly resolvedAt: string | null;
  readonly resolvedBy: string | null;
  readonly staffNote: string | null;
}

export interface SupportTicketPage {
  readonly items: readonly SupportTicket[];
  /** `null` 代表沒有下一頁。契約明講**不回傳總筆數**。 */
  readonly nextCursor: string | null;
}

export interface ListSupportTicketsQuery {
  readonly status?: SupportTicketStatus;
  readonly cursor?: string;
  /** 預設 20、上限 100（契約 §0.3）。 */
  readonly limit?: number;
}

/** 跟 `packages/api-client/src/endpoints/admin.ts` 同一招：具名介面沒有 index signature，
 * 要轉成 `ApiClient.get` 吃的 `query` 型別得先斷言一次。 */
type QueryRecord = Record<string, string | number | boolean | undefined | null>;

export function listSupportTickets(
  client: ApiClient,
  query: ListSupportTicketsQuery = {},
): Promise<SupportTicketPage> {
  return client.get<SupportTicketPage>('/v1/support/tickets', { query: query as QueryRecord });
}

export function getSupportTicket(client: ApiClient, id: string): Promise<SupportTicket> {
  return client.get<SupportTicket>(`/v1/support/tickets/${id}`);
}

export interface ResolveSupportTicketRequest {
  readonly staffNote?: string | null;
}

/** `Operator` 以上；`409 support.already-resolved` 代表別人剛處理過。 */
export function resolveSupportTicket(
  client: ApiClient,
  id: string,
  body: ResolveSupportTicketRequest = {},
): Promise<SupportTicket> {
  return client.post<SupportTicket>(`/v1/support/tickets/${id}/resolve`, { body });
}
