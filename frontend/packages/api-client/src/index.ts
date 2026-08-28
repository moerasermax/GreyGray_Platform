/**
 * `@greygray/api-client` —— 前端與後端之間的**唯一**通道。
 *
 * 元件不直接 `fetch`，不自己解 problem+json，不自己算金額。
 * 契約在 `docs/05-API契約.md` 與 `docs/api/openapi.*.yaml`。
 *
 * ── 型別從哪來 ──
 * `types.storefront.ts` 與 `types.admin.ts` 由 `pnpm generate` 從 OpenAPI 產出，
 * **不要手改**——手改了下次重新產生就沒了，而且會跟後端悄悄對不起來。
 * 契約要改請改 YAML（並回 `docs/05-API契約.md` 說明理由）。
 */

export * from './money';
export * from './problem';
export * from './idempotency';
export * from './http';
