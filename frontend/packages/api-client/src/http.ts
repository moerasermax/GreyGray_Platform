/**
 * HTTP 傳輸層。**所有對 BFF 的呼叫都要經過這裡**，不要在元件裡直接 `fetch`。
 *
 * 這一層負責四件事，每一件漏掉都會在別的地方變成 bug：
 *   1. `credentials: 'include'` —— session 走 HttpOnly cookie，少了這個就永遠是未登入
 *   2. 寫入請求自動帶 `Idempotency-Key`
 *   3. 非 2xx 一律轉成 {@link ApiError}，不讓 problem+json 的解析散在各處
 *   4. `platform.request-in-flight`（409）用**同一把 key** 自動重試
 */

import { newIdempotencyKey } from './idempotency';
import { ApiError, NetworkError, fallbackProblem, isProblemDetails } from './problem';

export interface ApiClientOptions {
  /** BFF 位址。Storefront 是 :5000，Admin 是 :5001。 */
  readonly baseUrl: string;
  /** 伺服器端渲染時要把瀏覽器的 cookie 轉發過去，用這個帶。 */
  readonly headers?: Record<string, string>;
  /** `platform.request-in-flight` 的重試次數。預設 2。 */
  readonly inFlightRetries?: number;
}

export interface RequestOptions {
  readonly query?: Record<string, string | number | boolean | undefined | null>;
  readonly body?: unknown;
  /**
   * 冪等鍵。寫入請求不給的話會自動產生一把——
   * 但**自動產生的那把在重試時會變**，所以「使用者可能會重試」的動作
   * 一定要自己傳 `IdempotencyScope.current()` 進來。
   */
  readonly idempotencyKey?: string;
  readonly signal?: AbortSignal;
  readonly headers?: Record<string, string>;
}

type Method = 'GET' | 'POST' | 'PATCH' | 'PUT' | 'DELETE';

const MUTATING: ReadonlySet<Method> = new Set<Method>(['POST', 'PATCH', 'PUT', 'DELETE']);

export class ApiClient {
  private readonly baseUrl: string;
  private readonly baseHeaders: Record<string, string>;
  private readonly inFlightRetries: number;

  constructor(options: ApiClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, '');
    this.baseHeaders = options.headers ?? {};
    this.inFlightRetries = options.inFlightRetries ?? 2;
  }

  get<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('GET', path, options);
  }

  post<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('POST', path, options);
  }

  patch<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('PATCH', path, options);
  }

  put<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('PUT', path, options);
  }

  delete<T>(path: string, options: RequestOptions = {}): Promise<T> {
    return this.request<T>('DELETE', path, options);
  }

  private async request<T>(method: Method, path: string, options: RequestOptions): Promise<T> {
    const url = this.buildUrl(path, options.query);
    const headers: Record<string, string> = {
      Accept: 'application/json, application/problem+json',
      ...this.baseHeaders,
      ...options.headers,
    };

    if (options.body !== undefined) {
      headers['Content-Type'] = 'application/json';
    }

    if (MUTATING.has(method)) {
      headers['Idempotency-Key'] = options.idempotencyKey ?? newIdempotencyKey();
    }

    // exactOptionalPropertyTypes 之下，`signal: undefined` 與「沒有 signal」是兩件事，
    // 所以要條件式加上去，不能寫 `signal: options.signal`。
    const init: RequestInit = {
      method,
      headers,
      // session 在 HttpOnly cookie 裡，少了這個就永遠是未登入
      credentials: 'include',
    };

    if (options.signal) {
      init.signal = options.signal;
    }

    if (options.body !== undefined) {
      init.body = JSON.stringify(options.body);
    }

    let attempt = 0;
    for (;;) {
      let response: Response;
      try {
        response = await fetch(url, init);
      } catch (cause) {
        if (cause instanceof DOMException && cause.name === 'AbortError') throw cause;
        throw new NetworkError(cause);
      }

      if (response.ok) {
        return (await this.readBody<T>(response)) as T;
      }

      const error = await this.toApiError(response);

      // 同一把 key 正在處理中：等一下用同一把 key 再試。
      // 換 key 重試是錯的——那會變成第二筆訂單。
      if (error.isInFlight && attempt < this.inFlightRetries) {
        attempt += 1;
        await delay(200 * 2 ** attempt);
        continue;
      }

      throw error;
    }
  }

  private buildUrl(
    path: string,
    query?: Record<string, string | number | boolean | undefined | null>,
  ): string {
    const url = new URL(`${this.baseUrl}${path.startsWith('/') ? path : `/${path}`}`);
    if (query) {
      for (const [key, value] of Object.entries(query)) {
        if (value !== undefined && value !== null && value !== '') {
          url.searchParams.set(key, String(value));
        }
      }
    }
    return url.toString();
  }

  private async readBody<T>(response: Response): Promise<T | undefined> {
    if (response.status === 204) return undefined;
    const text = await response.text();
    if (!text) return undefined;
    return JSON.parse(text) as T;
  }

  private async toApiError(response: Response): Promise<ApiError> {
    const traceId = response.headers.get('traceparent');
    try {
      const payload: unknown = await response.json();
      if (isProblemDetails(payload)) {
        return new ApiError(payload);
      }
    } catch {
      // 後端沒回合法 problem+json。這本身是異常，但前端不能因此炸掉。
    }
    return new ApiError(fallbackProblem(response.status, traceId));
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** 游標式分頁的回應形狀。所有列表端點都是這個（`docs/05-API契約.md` §5）。 */
export interface Page<T> {
  readonly items: T[];
  /** `null` 代表沒有下一頁。**不回傳總筆數**，要總數請改設計。 */
  readonly nextCursor: string | null;
}
