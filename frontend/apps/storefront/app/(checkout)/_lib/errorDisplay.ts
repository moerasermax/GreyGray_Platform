/**
 * 把 `@greygray/api-client` 丟出來的錯誤轉成畫面能直接用的訊息。
 * 集中在一處，避免每個頁面各自寫一套 `instanceof` 判斷、各自漏掉 `NetworkError`。
 */
import { ApiError, NetworkError } from '@greygray/api-client';

export interface ErrorDisplay {
  readonly title: string;
  readonly traceId: string | null;
}

/**
 * 網路中斷（`fetch` 直接失敗，連 HTTP 狀態碼都沒有）跟 API 回的錯誤是兩回事，
 * 都要顯示成看得懂的訊息，**不能白畫面**。
 */
export function describeError(error: unknown): ErrorDisplay {
  if (error instanceof ApiError) {
    return { title: error.problem.title, traceId: error.shortTraceId };
  }
  if (error instanceof NetworkError) {
    return { title: error.message, traceId: null };
  }
  return { title: '發生未預期的錯誤，請再試一次。', traceId: null };
}
