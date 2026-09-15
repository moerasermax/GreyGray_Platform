/**
 * 超商取貨門市（ADR-038，7-ELEVEN 電子地圖）的純函式與流程接線。
 *
 * ── 流程 ──
 * 結帳頁「選擇門市」→ 開選店票 → 隱藏表單**頂層**自動送出到綠界地圖（整頁離開）
 * → 綠界經瀏覽器 POST 回後端 → 後端 303 回 `/checkout?cvsSelection=<票>` 或 `?cvsSelectionError=<代碼>`
 * → 讀票顯示門市 → 送出訂單只帶 `convenienceStoreSelectionId`。
 *
 * ── 為什麼抽成純函式與「可注入 callback 的流程函式」──
 * 整頁離開再回來，頁面 state 全部消失，只剩 `checkoutDraft`（sessionStorage）與網址。
 * 進頁時要把兩者合併成一份初始狀態，**合併的規則不能靠 effect 的執行順序**——
 * 那樣照做會把草稿蓋成空的、或把票弄丟。規則寫成純函式，錯誤分支寫成
 * 注入 API 與 state callback 的函式，才測得到接線（這個 workspace 沒有 jsdom）。
 *
 * ── 門市名稱與地址只放在記憶體 ──
 * 草稿只存選店票 id；名稱地址每次都跟後端讀。不放網址、不放 localStorage（ADR-038）。
 */
import { ApiError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { emptyCheckoutDraft, isCvsSelectionId, type CheckoutDraft } from './checkoutDraft';
import { describeError, type ErrorDisplay } from './errorDisplay';

type S = components['schemas'];

export const CVS_SELECTION_PARAM = 'cvsSelection';
export const CVS_SELECTION_ERROR_PARAM = 'cvsSelectionError';

/** 派工書 §0.1 那張表（後端 `docs/52` §0.3 定的代碼），**一字不差**。 */
export const CVS_SELECTION_MESSAGES = {
  expired: '門市選擇已逾時，請重新選擇門市。',
  alreadyUsed: '這次的門市選擇已經用過了，請重新選擇門市。',
  invalidReply: '門市資料驗證失敗，請重新選擇門市。',
  notConfigured: '目前暫停超商取貨，請改選其他配送方式。',
  unavailable: '暫時無法取得門市資料，請稍後再試。',
  other: '門市選擇沒有完成，請重新選擇門市。',
} as const;

const MESSAGE_BY_CODE: Readonly<Record<string, string>> = {
  expired: CVS_SELECTION_MESSAGES.expired,
  'already-used': CVS_SELECTION_MESSAGES.alreadyUsed,
  'invalid-reply': CVS_SELECTION_MESSAGES.invalidReply,
  'not-configured': CVS_SELECTION_MESSAGES.notConfigured,
  unavailable: CVS_SELECTION_MESSAGES.unavailable,
};

/** 回程錯誤代碼 → 給客人看的訊息。不認得的代碼走「其他」，不把代碼原樣秀出來。 */
export function cvsSelectionErrorMessage(code: string): string {
  return Object.hasOwn(MESSAGE_BY_CODE, code) ? MESSAGE_BY_CODE[code]! : CVS_SELECTION_MESSAGES.other;
}

// ── 1. 讀回程參數 ─────────────────────────────────────────────────────────

/** `URLSearchParams` 與 Next 的 `ReadonlyURLSearchParams` 都符合。 */
export interface SearchParamsLike {
  get(name: string): string | null;
}

export type CvsReturn =
  | { readonly kind: 'selected'; readonly selectionId: string }
  | { readonly kind: 'error'; readonly message: string }
  | { readonly kind: 'malformed' }
  | { readonly kind: 'none' };

/**
 * - 兩個參數同時出現：以 `cvsSelectionError` 為準。
 * - `cvsSelection` 不是 20 個英數 → `malformed`：那是被亂改的網址，不打後端、不顯示錯誤，但網址一樣要清。
 * - 兩個都沒有 → `none`（網址不動）。
 */
export function parseCvsReturn(query: SearchParamsLike): CvsReturn {
  const errorCode = query.get(CVS_SELECTION_ERROR_PARAM);
  if (errorCode !== null) return { kind: 'error', message: cvsSelectionErrorMessage(errorCode) };

  const selectionId = query.get(CVS_SELECTION_PARAM);
  if (selectionId === null) return { kind: 'none' };
  return isCvsSelectionId(selectionId) ? { kind: 'selected', selectionId } : { kind: 'malformed' };
}

// ── 2. 進頁合併 ───────────────────────────────────────────────────────────

export interface CheckoutEntry {
  /** 一次設定進 state 的初始欄位。 */
  readonly draft: CheckoutDraft;
  /** 要顯示在門市區塊的錯誤訊息。 */
  readonly errorMessage: string | null;
  /** 有草稿或有回程參數時，把合併後的**完整**草稿存回去（不是只存選店票）。 */
  readonly shouldSaveDraft: boolean;
  /** 回程結果不是 `none`（含 `malformed`）就清網址。 */
  readonly shouldClearUrl: boolean;
}

export function mergeCheckoutEntry(draft: CheckoutDraft | null, cvsReturn: CvsReturn): CheckoutEntry {
  const base = draft ?? emptyCheckoutDraft();
  const shouldClearUrl = cvsReturn.kind !== 'none';
  const shouldSaveDraft = draft !== null || shouldClearUrl;

  switch (cvsReturn.kind) {
    case 'selected':
      return {
        draft: { ...base, convenienceStoreSelectionId: cvsReturn.selectionId },
        errorMessage: null,
        shouldSaveDraft,
        shouldClearUrl,
      };
    case 'error':
      // 畫面叫人重選，就不能還送得出草稿裡的舊門市。
      return { draft: clearCvsSelection(base), errorMessage: cvsReturn.message, shouldSaveDraft, shouldClearUrl };
    default:
      return { draft: base, errorMessage: null, shouldSaveDraft, shouldClearUrl };
  }
}

// ── 3. 只清選店票 ─────────────────────────────────────────────────────────

/**
 * 其他欄位不動、選店票為 `null`。
 * **404／422 清票用這個，不要用 `clearCheckoutDraft`**——那會連留言一起刪。
 */
export function clearCvsSelection(draft: CheckoutDraft): CheckoutDraft {
  return { ...draft, convenienceStoreSelectionId: null };
}

// ── 4. 地圖表單 ───────────────────────────────────────────────────────────

export interface HiddenField {
  readonly name: string;
  readonly value: string;
}

/** `CvsMapSession.fields` 原封不動轉成隱藏欄位：不排序、不過濾、不改值（`ExtraData` 就是選店票）。 */
export function cvsMapFormFields(session: Pick<S['CvsMapSession'], 'fields'>): HiddenField[] {
  return Object.entries(session.fields).map(([name, value]) => ({ name, value }));
}

// ── 5. 開始選門市：鎖與錯誤 ─────────────────────────────────────────────

/** 開票失敗時給客人看的訊息。機器讀 `code`，不讀 `title`。 */
export function cvsMapSessionErrorMessage(cause: unknown): string {
  if (cause instanceof ApiError && cause.is('logistics.not-configured')) {
    return CVS_SELECTION_MESSAGES.notConfigured;
  }
  return describeError(cause).title;
}

export interface PendingRef {
  current: boolean;
}

export interface ExecuteCvsMapStartOptions {
  readonly pendingRef: PendingRef;
  readonly start: () => Promise<S['CvsMapSession']>;
  readonly onStarting: () => void;
  readonly onSession: (session: S['CvsMapSession']) => void;
  readonly onError: (message: string) => void;
}

/**
 * 按下「選擇 7-ELEVEN 門市」。
 * `pendingRef` 在任何 `await` 之前**同步**上鎖——`setState('loading')` 不是鎖，
 * React 還沒重繪時的同步連點會開出兩張票（FE-32 的教訓）。
 *
 * **成功不放鎖**：接下來是整頁導向，放掉會讓人在導向完成前的空隙再開一張票。
 * 客人從地圖按「上一頁」回來時由 {@link resetCvsMapStartOnPageShow} 放鎖。
 *
 * @returns 這一次有沒有真的開始（被鎖擋下回 `false`）。
 */
export async function executeCvsMapStart(options: ExecuteCvsMapStartOptions): Promise<boolean> {
  if (options.pendingRef.current) return false;
  options.pendingRef.current = true;
  options.onStarting();

  let session: S['CvsMapSession'];
  try {
    session = await options.start();
  } catch (cause) {
    options.pendingRef.current = false;
    options.onError(cvsMapSessionErrorMessage(cause));
    return true;
  }
  options.onSession(session);
  return true;
}

/** 從 bfcache 還原（客人在地圖頁按「上一頁」）才要重置；一般載入不動。 */
export function shouldResetOnPageShow(event: { readonly persisted: boolean }): boolean {
  return event.persisted;
}

/**
 * `pageshow` 的處理：bfcache 還原時畫面停在「正在前往地圖」、鎖還握著、隱藏表單還在，
 * 按鈕再也按不動。這裡放鎖並讓呼叫端清掉地圖表單狀態。
 */
export function resetCvsMapStartOnPageShow(
  event: { readonly persisted: boolean },
  pendingRef: PendingRef,
  onReset: () => void,
): void {
  if (!shouldResetOnPageShow(event)) return;
  pendingRef.current = false;
  onReset();
}

/**
 * 頁面給元件的 `onStart`：**先存完整草稿，再開票**。
 * 整頁離開之後頁面 state 全部消失，不存的話回來配送方式與留言都沒了。
 * 不帶 `device`，讓綠界自己判斷電腦版或手機版。
 */
export async function startCvsMapSession(options: {
  readonly saveDraft: () => void;
  readonly createSession: () => Promise<S['CvsMapSession']>;
}): Promise<S['CvsMapSession']> {
  options.saveDraft();
  return options.createSession();
}

// ── 6. 讀票 ───────────────────────────────────────────────────────────────

export interface LoadCvsSelectionOptions {
  readonly getSelection: (selectionId: string) => Promise<S['CvsStoreSelection']>;
  /** 回來時這一次讀取是否仍是最新的（避免舊請求蓋掉新狀態）。 */
  readonly isCurrent: () => boolean;
  readonly setLoading: (loading: boolean) => void;
  readonly setSelection: (selection: S['CvsStoreSelection'] | null) => void;
  readonly setError: (message: string | null) => void;
  /** 只清選店票（state 與草稿），其他欄位不動。 */
  readonly clearSelectionId: () => void;
}

/**
 * - 讀到 → 顯示。
 * - `404`（不存在、過期、屬於別台購物車，後端不區分）→ 只清選店票、顯示逾時那句。
 * - 其他錯誤 → 顯示「暫時無法取得」，**選店票保留**，重整可以再試。
 */
export async function loadCvsSelection(selectionId: string, options: LoadCvsSelectionOptions): Promise<void> {
  options.setLoading(true);
  options.setError(null);
  try {
    const selection = await options.getSelection(selectionId);
    if (!options.isCurrent()) return;
    options.setSelection(selection);
  } catch (cause) {
    if (!options.isCurrent()) return;
    options.setSelection(null);
    if (cause instanceof ApiError && cause.status === 404) {
      options.clearSelectionId();
      options.setError(CVS_SELECTION_MESSAGES.expired);
    } else {
      options.setError(CVS_SELECTION_MESSAGES.unavailable);
    }
  } finally {
    if (options.isCurrent()) options.setLoading(false);
  }
}

// ── 7. 送出訂單 ───────────────────────────────────────────────────────────

/** 只有超商取貨才送選店票；換成別的配送方式時不清票（切回來還在），但不送。 */
export function checkoutStoreSelectionId(
  deliveryMethod: S['DeliveryMethod'] | null,
  selectionId: string | null,
): string | null {
  return deliveryMethod === 'ConvenienceStore' ? selectionId : null;
}

export interface CheckoutFailureOptions {
  /** 401：存草稿並導去登入。`submitting` 不放——導向是非同步的。 */
  readonly onUnauthorized: () => void;
  readonly clearSelectionId: () => void;
  readonly setStoreError: (message: string) => void;
  readonly setSubmitError: (error: ErrorDisplay) => void;
  readonly setSubmitting: (submitting: boolean) => void;
}

/** 送出訂單失敗的分支。`422 checkout.store-selection-expired` → 只清選店票、顯示逾時那句、放開 `submitting`。 */
export function handleCheckoutFailure(cause: unknown, options: CheckoutFailureOptions): void {
  if (cause instanceof ApiError && cause.isUnauthorized) {
    options.onUnauthorized();
    return;
  }
  if (cause instanceof ApiError && cause.is('checkout.store-selection-expired')) {
    options.clearSelectionId();
    options.setStoreError(CVS_SELECTION_MESSAGES.expired);
    options.setSubmitting(false);
    return;
  }
  options.setSubmitError(describeError(cause));
  options.setSubmitting(false);
}
