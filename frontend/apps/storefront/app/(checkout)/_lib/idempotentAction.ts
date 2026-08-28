/**
 * Checkout 保留這個本地入口，既有測試與頁面不必知道共用 helper 的實體位置。
 * 真正實作放在 api-client，Admin 與 Storefront 的所有 mutation 共用同一套語意。
 */
export { createIdempotentAction, createPayloadIdempotentAction } from '@greygray/api-client';
export type { IdempotentAction, PayloadIdempotentAction } from '@greygray/api-client';
