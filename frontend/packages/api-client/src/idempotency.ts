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
