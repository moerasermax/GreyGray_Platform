/**
 * 冪等鍵。契約見 `docs/05-API契約.md` §4。
 *
 * 所有寫入請求都必須帶 `Idempotency-Key`，沒帶後端直接回 400。
 *
 * **同一個使用者動作用同一把 key，重試時不變。** 這正是冪等的意義：
 * 按鈕連點兩下、網路逾時後自動重送，用的都必須是同一把 key，
 * 後端才有辦法認出「這是同一次」而不是「這是第二次」。
 *
 * 反過來，使用者**重新發起**一次動作（改了數量再送一次）要換新 key。
 */

/** 產生一把新的冪等鍵。 */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  // 極舊環境的保底。正常不會走到。
  return `${Date.now().toString(16)}-${Math.random().toString(16).slice(2, 14)}`;
}

/**
 * 綁在「一次使用者動作」上的冪等鍵持有器。
 *
 * 用法：表單載入時建一個，整個送出流程（含重試）共用，成功後才 {@link IdempotencyScope.reset}。
 *
 * ```ts
 * const scope = createIdempotencyScope();
 * async function submit() {
 *   await api.checkout(body, { idempotencyKey: scope.current() });  // 重試時 key 不變
 *   scope.reset();                                                  // 下一次是新動作
 * }
 * ```
 */
export interface IdempotencyScope {
  /** 目前這次動作的 key。多次呼叫回傳同一個值。 */
  current(): string;
  /** 動作完成，下一次要換新的 key。 */
  reset(): void;
}

export function createIdempotencyScope(): IdempotencyScope {
  let key = newIdempotencyKey();
  return {
    current: () => key,
    reset: () => {
      key = newIdempotencyKey();
    },
  };
}

/**
 * 依 mutation payload 維持冪等鍵。
 *
 * 同一份 payload（連點、逾時重試）拿到同一把 key；使用者修改欄位後 payload 改變，
 * 自動換 key，避免後端回 `platform.idempotency-key-reused`。成功後呼叫 complete，
 * 下一次即使 payload 一樣也會被視為新的使用者動作。
 */
export interface PayloadIdempotencyScope {
  current(payload: unknown): string;
  complete(): void;
}

export function createPayloadIdempotencyScope(): PayloadIdempotencyScope {
  const scope = createIdempotencyScope();
  let fingerprint: string | null = null;

  return {
    current(payload: unknown) {
      const nextFingerprint = payloadFingerprint(payload);
      if (fingerprint !== null && fingerprint !== nextFingerprint) {
        scope.reset();
      }
      fingerprint = nextFingerprint;
      return scope.current();
    },
    complete() {
      fingerprint = null;
      scope.reset();
    },
  };
}

/**
 * 把一次使用者動作包成可安全重試、可防連點的非同步函式。
 *
 * 同一時間的重複呼叫共用同一個 in-flight promise，因此不會送出多個 HTTP request；
 * 失敗時保留原 key 供重試，成功後才換 key 給下一次新動作。
 */
export interface IdempotentAction<TResult> {
  currentKey(): string;
  run(): Promise<TResult>;
}

export function createIdempotentAction<TResult>(
  action: (idempotencyKey: string) => Promise<TResult>,
): IdempotentAction<TResult> {
  const scope = createIdempotencyScope();
  let pending: Promise<TResult> | null = null;

  function run(): Promise<TResult> {
    if (pending) return pending;

    const key = scope.current();
    pending = action(key)
      .then((result) => {
        scope.reset();
        return result;
      })
      .finally(() => {
        pending = null;
      });
    return pending;
  }

  return { currentKey: () => scope.current(), run };
}

/**
 * payload 可能在失敗後被使用者修改的 mutation action。
 *
 * - 同 payload 的連點共用同一個 request 與 key。
 * - 同 payload 失敗後重試仍沿用 key。
 * - payload 改變會換 key，避免後端回 `platform.idempotency-key-reused`。
 * - request 還在執行時不接受不同 payload；UI 應維持送出中狀態，不能偷偷再送第二筆。
 */
export interface PayloadIdempotentAction<TPayload, TResult> {
  currentKey(payload: TPayload): string;
  run(payload: TPayload): Promise<TResult>;
}

export function createPayloadIdempotentAction<TPayload, TResult>(
  action: (payload: TPayload, idempotencyKey: string) => Promise<TResult>,
): PayloadIdempotentAction<TPayload, TResult> {
  const scope = createPayloadIdempotencyScope();
  let pending: { readonly fingerprint: string; readonly promise: Promise<TResult> } | null = null;

  function run(payload: TPayload): Promise<TResult> {
    const fingerprint = payloadFingerprint(payload);
    if (pending) {
      if (pending.fingerprint === fingerprint) return pending.promise;
      return Promise.reject(new Error('上一筆寫入仍在處理中，不能同時送出不同內容。'));
    }

    const key = scope.current(payload);
    const promise = action(payload, key)
      .then((result) => {
        scope.complete();
        return result;
      })
      .finally(() => {
        pending = null;
      });
    pending = { fingerprint, promise };
    return promise;
  }

  return { currentKey: (payload) => scope.current(payload), run };
}

function payloadFingerprint(payload: unknown): string {
  return JSON.stringify(payload) ?? String(payload);
}
