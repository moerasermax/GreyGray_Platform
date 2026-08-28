/**
 * RFC 9457 Problem Details。契約見 `docs/05-API契約.md` §3。
 *
 * **機器讀 `code`，不要讀 `title`。**
 * `title` 是給人看的繁體中文，客服可以直接照念，但它會被改寫；
 * `code` 一經上線不可改名，是穩定的。
 */

export interface ProblemDetails {
  readonly type: string;
  /** 繁體中文，可以直接顯示給客人看。 */
  readonly title: string;
  readonly status: number;
  readonly detail?: string | null;
  readonly instance?: string | null;
  /** 對應後端 `Error.Code`，格式 `{模組}.{kebab-case}`。**判斷用這個。** */
  readonly code: string;
  /** W3C traceparent。錯誤畫面上顯示後 8 碼，讓客人可以報給客服。 */
  readonly traceId?: string | null;
  /** 只有欄位驗證失敗（422）才有。key 是欄位名。 */
  readonly errors?: Record<string, string[]> | null;
}

/** API 回非 2xx 時丟這個。網路層的失敗丟 {@link NetworkError}。 */
export class ApiError extends Error {
  readonly problem: ProblemDetails;

  constructor(problem: ProblemDetails) {
    super(problem.title);
    this.name = 'ApiError';
    this.problem = problem;
  }

  get code(): string {
    return this.problem.code;
  }

  get status(): number {
    return this.problem.status;
  }

  /** 欄位驗證錯誤，直接餵給表單。 */
  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {};
  }

  /** 錯誤畫面上給客人看的那串。客服拿它就能查到整條鏈。 */
  get shortTraceId(): string | null {
    const traceId = this.problem.traceId;
    return traceId ? traceId.slice(-8) : null;
  }

  is(code: string): boolean {
    return this.problem.code === code;
  }

  /** 是不是「同一把 key 正在處理中」——這種要稍後用**同一把 key** 重試。 */
  get isInFlight(): boolean {
    return this.problem.code === 'platform.request-in-flight';
  }

  get isUnauthorized(): boolean {
    return this.problem.status === 401;
  }
}

export class NetworkError extends Error {
  constructor(cause: unknown) {
    super('連線失敗，請檢查網路後再試一次。');
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/**
 * 後端沒回合法 problem+json 時的保底。
 *
 * 這種情況本身就是異常（BFF 應該永遠回 problem+json），
 * 但前端不能因此炸掉——白畫面比錯誤訊息糟得多。
 */
export function fallbackProblem(status: number, traceId?: string | null): ProblemDetails {
  return {
    type: 'https://greygray.tw/errors/platform.unexpected',
    title: status >= 500 ? '系統發生問題，請稍後再試。' : '請求無法完成，請再試一次。',
    status,
    code: 'platform.unexpected',
    traceId: traceId ?? null,
  };
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return typeof candidate.code === 'string' && typeof candidate.title === 'string';
}
