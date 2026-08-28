/**
 * RFC 9457 problem+json 的 mock 版本，形狀對齊 `docs/05-API契約.md` §3。
 *
 * `errorHandler()` 是**任何端點都能用**的通用 500／409／422 產生器——
 * 不用每個端點都手寫一份，方法 ＋ 路徑 ＋ 想要的 problem 即可組出一支 MSW handler，
 * 用 `server.use(...)` / `worker.use(...)` 疊加到預設的成功回應之上，模擬「這一次剛好出錯」。
 */

import { HttpResponse, http, type HttpHandler } from 'msw';

export interface MockProblem {
  readonly type: string;
  readonly title: string;
  readonly status: number;
  readonly code: string;
  readonly detail?: string | null;
  readonly instance?: string | null;
  readonly traceId?: string | null;
  readonly errors?: Record<string, string[]> | null;
}

export function problem(
  status: number,
  code: string,
  title: string,
  extra: Partial<Omit<MockProblem, 'status' | 'code' | 'title'>> = {},
): MockProblem {
  return {
    type: `https://greygray.tw/errors/${code}`,
    title,
    status,
    code,
    traceId: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01',
    ...extra,
  };
}

export const SERVER_ERROR: MockProblem = problem(500, 'platform.unexpected', '系統發生問題，請稍後再試。');

type Method = 'get' | 'post' | 'patch' | 'put' | 'delete';

/** 任意端點 → 一支回傳 `problem` 的 handler。給錯誤情境測試用，不進預設的成功清單。 */
export function errorHandler(method: Method, url: string, mockProblem: MockProblem): HttpHandler {
  return http[method](url, () => HttpResponse.json(mockProblem, { status: mockProblem.status }));
}

export function jsonProblem(mockProblem: MockProblem) {
  return HttpResponse.json(mockProblem, { status: mockProblem.status });
}
