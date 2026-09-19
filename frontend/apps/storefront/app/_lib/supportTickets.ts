/**
 * 客服工單（ADR-040）契約型別與呼叫——不進 `packages/api-client`（FE-34 正在動那個套件），
 * 等這一波收完由 Leader 決定要不要收編。
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

export interface CreateSupportTicketRequest {
  readonly message: string;
  readonly contactEmail?: string | null;
  readonly contactPhone?: string | null;
  readonly menuPath?: readonly string[];
  readonly orderId?: string | null;
}

/** 匿名可打，`POST /v1/support/tickets`。 */
export function createSupportTicket(
  client: ApiClient,
  request: CreateSupportTicketRequest,
): Promise<SupportTicket> {
  return client.post<SupportTicket>('/v1/support/tickets', { body: request });
}
