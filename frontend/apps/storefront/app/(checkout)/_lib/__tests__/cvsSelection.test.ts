/**
 * 超商門市（ADR-038）：回程參數、進頁合併、只清選店票、錯誤訊息、地圖表單欄位，
 * 以及頁面與元件的流程接線（注入假的 API 與 state callback）。
 *
 * 沒有 jsdom，所以流程都寫成純函式或「可注入 callback 的函式」，在這裡直接驅動。
 */
import { describe, expect, it, vi } from 'vitest';
import { ApiError, NetworkError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import {
  emptyCheckoutDraft,
  loadCheckoutDraft,
  saveCheckoutDraft,
  type CheckoutDraft,
  type DraftStorage,
} from '../checkoutDraft';
import {
  CVS_SELECTION_MESSAGES,
  checkoutStoreSelectionId,
  clearCvsSelection,
  cvsMapFormFields,
  cvsMapSessionErrorMessage,
  cvsSelectionErrorMessage,
  executeCvsMapStart,
  handleCheckoutFailure,
  loadCvsSelection,
  mergeCheckoutEntry,
  parseCvsReturn,
  resetCvsMapStartOnPageShow,
  shouldResetOnPageShow,
  startCvsMapSession,
  type CvsReturn,
} from '../cvsSelection';

type S = components['schemas'];

const OLD_TICKET = 'OLDTICKET00000000000';
const NEW_TICKET = 'NEWTICKET11111111111';

const DRAFT: CheckoutDraft = {
  deliveryMethod: 'ConvenienceStore',
  shippingPolicy: 'ShipSeparately',
  shippingAddressId: null,
  convenienceStoreSelectionId: OLD_TICKET,
  buyerNote: '麻煩包好一點',
};

const SELECTION: S['CvsStoreSelection'] = {
  selectionId: NEW_TICKET,
  storeCode: '991234',
  storeName: '7-ELEVEN 信義門市',
  storeAddress: '台北市信義區松仁路 100 號',
  isOutlying: false,
  expiresAt: '2026-09-15T10:15:00Z',
};

const SESSION: S['CvsMapSession'] = {
  selectionId: NEW_TICKET,
  method: 'POST',
  action: 'https://logistics-stage.ecpay.com.tw/Express/map',
  fields: { MerchantID: '2000933', ExtraData: NEW_TICKET },
  expiresAt: '2026-09-15T10:15:00Z',
};

function apiError(status: number, code: string): ApiError {
  return new ApiError({ type: 'about:blank', title: `後端的 ${code}`, status, code, traceId: null });
}

function query(params: Record<string, string>): URLSearchParams {
  return new URLSearchParams(params);
}

function fakeStorage(): DraftStorage {
  const map = new Map<string, string>();
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => void map.set(key, value),
    removeItem: (key) => void map.delete(key),
    get length() {
      return map.size;
    },
    key: (index) => [...map.keys()][index] ?? null,
  };
}

// ── 回程參數 ─────────────────────────────────────────────────────────────

describe('parseCvsReturn：地圖導回結帳頁的網址', () => {
  it('正常的 20 字英數票 → selected', () => {
    expect(parseCvsReturn(query({ cvsSelection: NEW_TICKET }))).toEqual({ kind: 'selected', selectionId: NEW_TICKET });
  });

  it.each([
    ['19 字', 'ABCDEFGHIJ012345678'],
    ['21 字', 'ABCDEFGHIJ01234567890'],
    ['含 -', 'ABCDEFGHIJ-123456789'],
    ['空字串', ''],
  ])('%s → malformed（不打後端、不顯示錯誤）', (_label, value) => {
    expect(parseCvsReturn(query({ cvsSelection: value }))).toEqual({ kind: 'malformed' });
  });

  it('沒有參數 → none', () => {
    expect(parseCvsReturn(query({}))).toEqual({ kind: 'none' });
    expect(parseCvsReturn(query({ next: '/cart' }))).toEqual({ kind: 'none' });
  });

  it.each([
    ['expired', '門市選擇已逾時，請重新選擇門市。'],
    ['already-used', '這次的門市選擇已經用過了，請重新選擇門市。'],
    ['invalid-reply', '門市資料驗證失敗，請重新選擇門市。'],
    ['not-configured', '目前暫停超商取貨，請改選其他配送方式。'],
    ['unavailable', '暫時無法取得門市資料，請稍後再試。'],
  ])('錯誤代碼 %s → 派工書 §0.1 那句', (code, message) => {
    expect(parseCvsReturn(query({ cvsSelectionError: code }))).toEqual({ kind: 'error', message });
    expect(cvsSelectionErrorMessage(code)).toBe(message);
  });

  it.each([['whatever'], [''], ['EXPIRED'], ['toString'], ['__proto__']])('未知代碼 %j → 「其他」那句', (code) => {
    expect(cvsSelectionErrorMessage(code)).toBe('門市選擇沒有完成，請重新選擇門市。');
  });

  it('兩個參數同時出現：以 cvsSelectionError 為準', () => {
    expect(parseCvsReturn(query({ cvsSelection: NEW_TICKET, cvsSelectionError: 'expired' }))).toEqual({
      kind: 'error',
      message: CVS_SELECTION_MESSAGES.expired,
    });
  });
});

// ── 進頁合併 ─────────────────────────────────────────────────────────────

describe('mergeCheckoutEntry：草稿 × 回程結果', () => {
  const selected: CvsReturn = { kind: 'selected', selectionId: NEW_TICKET };
  const failed: CvsReturn = { kind: 'error', message: CVS_SELECTION_MESSAGES.invalidReply };

  it('草稿有舊票 ＋ 回程新票 → 新票，其他欄位來自草稿；存草稿、清網址', () => {
    expect(mergeCheckoutEntry(DRAFT, selected)).toEqual({
      draft: { ...DRAFT, convenienceStoreSelectionId: NEW_TICKET },
      errorMessage: null,
      shouldSaveDraft: true,
      shouldClearUrl: true,
    });
  });

  it('草稿有舊票 ＋ 回程錯誤 → 票為 null（不能還送得出舊門市）、帶錯誤訊息；存草稿、清網址', () => {
    expect(mergeCheckoutEntry(DRAFT, failed)).toEqual({
      draft: { ...DRAFT, convenienceStoreSelectionId: null },
      errorMessage: CVS_SELECTION_MESSAGES.invalidReply,
      shouldSaveDraft: true,
      shouldClearUrl: true,
    });
  });

  it('草稿有票 ＋ malformed → 草稿的票、不顯示錯誤；網址一樣要清', () => {
    expect(mergeCheckoutEntry(DRAFT, { kind: 'malformed' })).toEqual({
      draft: DRAFT,
      errorMessage: null,
      shouldSaveDraft: true,
      shouldClearUrl: true,
    });
  });

  it('草稿有票 ＋ none → 草稿的票；網址不動（草稿照樣存回）', () => {
    expect(mergeCheckoutEntry(DRAFT, { kind: 'none' })).toEqual({
      draft: DRAFT,
      errorMessage: null,
      shouldSaveDraft: true,
      shouldClearUrl: false,
    });
  });

  it('沒有草稿 ＋ 回程新票 → 只有票；存草稿、清網址', () => {
    expect(mergeCheckoutEntry(null, selected)).toEqual({
      draft: { ...emptyCheckoutDraft(), convenienceStoreSelectionId: NEW_TICKET },
      errorMessage: null,
      shouldSaveDraft: true,
      shouldClearUrl: true,
    });
  });

  it('沒有草稿 ＋ none → 什麼都不存、網址不動', () => {
    expect(mergeCheckoutEntry(null, { kind: 'none' })).toEqual({
      draft: emptyCheckoutDraft(),
      errorMessage: null,
      shouldSaveDraft: false,
      shouldClearUrl: false,
    });
  });
});

// ── 只清選店票 ───────────────────────────────────────────────────────────

describe('clearCvsSelection：只清選店票', () => {
  it('留言等其他欄位不動，選店票為 null', () => {
    expect(clearCvsSelection(DRAFT)).toEqual({ ...DRAFT, convenienceStoreSelectionId: null });
  });

  it('存回 storage 之後讀出來，留言還在', () => {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', DRAFT, storage);
    saveCheckoutDraft('cart_1', clearCvsSelection(loadCheckoutDraft('cart_1', storage)!), storage);
    expect(loadCheckoutDraft('cart_1', storage)).toEqual({ ...DRAFT, convenienceStoreSelectionId: null });
  });
});

// ── 地圖表單欄位 ─────────────────────────────────────────────────────────

describe('cvsMapFormFields：原封不動', () => {
  it('不排序、不過濾、不改值', () => {
    const fields = { Zeta: 'z', MerchantID: '2000933', ExtraData: NEW_TICKET, Alpha: '', Weird: 'a"b<c&d' };
    expect(cvsMapFormFields({ fields })).toEqual([
      { name: 'Zeta', value: 'z' },
      { name: 'MerchantID', value: '2000933' },
      { name: 'ExtraData', value: NEW_TICKET },
      { name: 'Alpha', value: '' },
      { name: 'Weird', value: 'a"b<c&d' },
    ]);
  });
});

// ── 開始選門市 ───────────────────────────────────────────────────────────

describe('cvsMapSessionErrorMessage', () => {
  it('503 logistics.not-configured → 「目前暫停超商取貨」那句', () => {
    expect(cvsMapSessionErrorMessage(apiError(503, 'logistics.not-configured'))).toBe(
      '目前暫停超商取貨，請改選其他配送方式。',
    );
  });

  it('其他失敗走 describeError', () => {
    expect(cvsMapSessionErrorMessage(apiError(500, 'platform.unexpected'))).toBe('後端的 platform.unexpected');
    expect(cvsMapSessionErrorMessage(new NetworkError('fetch failed'))).toBe('連線失敗，請檢查網路後再試一次。');
  });
});

describe('executeCvsMapStart：開始選門市的鎖', () => {
  function harness(start: () => Promise<S['CvsMapSession']>) {
    const pendingRef = { current: false };
    const onStarting = vi.fn();
    const onSession = vi.fn();
    const onError = vi.fn();
    const run = () => executeCvsMapStart({ pendingRef, start, onStarting, onSession, onError });
    return { pendingRef, onStarting, onSession, onError, run };
  }

  it('同步連叫兩次只打一次 createCvsMapSession', async () => {
    let resolve!: (session: S['CvsMapSession']) => void;
    const start = vi.fn(() => new Promise<S['CvsMapSession']>((r) => (resolve = r)));
    const h = harness(start);

    const first = h.run();
    const second = h.run();
    expect(h.pendingRef.current).toBe(true);
    resolve(SESSION);

    expect(await first).toBe(true);
    expect(await second).toBe(false);
    expect(start).toHaveBeenCalledTimes(1);
    expect(h.onSession).toHaveBeenCalledWith(SESSION);
  });

  it('成功之後不放鎖（接下來是整頁導向）', async () => {
    const h = harness(() => Promise.resolve(SESSION));
    await h.run();
    expect(h.pendingRef.current).toBe(true);
  });

  it('503 → not-configured 那句，放開鎖', async () => {
    const h = harness(() => Promise.reject(apiError(503, 'logistics.not-configured')));
    await h.run();
    expect(h.onError).toHaveBeenCalledWith(CVS_SELECTION_MESSAGES.notConfigured);
    expect(h.pendingRef.current).toBe(false);
  });

  it('其他失敗 → 可以再按，第二次真的會再打一次', async () => {
    const start = vi
      .fn<() => Promise<S['CvsMapSession']>>()
      .mockRejectedValueOnce(new NetworkError('網路連線中斷'))
      .mockResolvedValueOnce(SESSION);
    const h = harness(start);

    await h.run();
    expect(h.onError).toHaveBeenCalledWith('連線失敗，請檢查網路後再試一次。');
    expect(h.pendingRef.current).toBe(false);

    expect(await h.run()).toBe(true);
    expect(start).toHaveBeenCalledTimes(2);
    expect(h.onSession).toHaveBeenCalledWith(SESSION);
  });
});

describe('pageshow：客人在地圖頁按「上一頁」', () => {
  it('bfcache 還原（persisted）→ 放鎖、清掉地圖表單狀態，按鈕可以再按', async () => {
    const pendingRef = { current: false };
    const start = vi.fn(() => Promise.resolve(SESSION));
    await executeCvsMapStart({ pendingRef, start, onStarting: () => {}, onSession: () => {}, onError: () => {} });
    expect(pendingRef.current).toBe(true);

    const onReset = vi.fn();
    resetCvsMapStartOnPageShow({ persisted: true }, pendingRef, onReset);
    expect(pendingRef.current).toBe(false);
    expect(onReset).toHaveBeenCalledTimes(1);

    await executeCvsMapStart({ pendingRef, start, onStarting: () => {}, onSession: () => {}, onError: () => {} });
    expect(start).toHaveBeenCalledTimes(2);
  });

  it('一般載入（沒有 persisted）→ 什麼都不動', () => {
    const pendingRef = { current: true };
    const onReset = vi.fn();
    resetCvsMapStartOnPageShow({ persisted: false }, pendingRef, onReset);
    expect(shouldResetOnPageShow({ persisted: false })).toBe(false);
    expect(pendingRef.current).toBe(true);
    expect(onReset).not.toHaveBeenCalled();
  });
});

describe('startCvsMapSession：頁面給元件的 onStart', () => {
  it('先存草稿、再開票，回傳開票結果', async () => {
    const order: string[] = [];
    const session = await startCvsMapSession({
      saveDraft: () => order.push('save'),
      createSession: () => {
        order.push('create');
        return Promise.resolve(SESSION);
      },
    });
    expect(order).toEqual(['save', 'create']);
    expect(session).toBe(SESSION);
  });
});

// ── 讀票 ─────────────────────────────────────────────────────────────────

describe('loadCvsSelection：流程接線', () => {
  /** 模擬頁面：state ＋ sessionStorage 草稿，`clearSelectionId` 照頁面的做法「只清選店票」。 */
  function page(getSelection: (id: string) => Promise<S['CvsStoreSelection']>) {
    const storage = fakeStorage();
    saveCheckoutDraft('cart_1', { ...DRAFT, convenienceStoreSelectionId: NEW_TICKET }, storage);
    const state = {
      selectionId: NEW_TICKET as string | null,
      selection: null as S['CvsStoreSelection'] | null,
      loading: false,
      error: null as string | null,
      loadingHistory: [] as boolean[],
    };
    const run = (isCurrent: () => boolean = () => true) =>
      loadCvsSelection(NEW_TICKET, {
        getSelection,
        isCurrent,
        setLoading: (loading) => {
          state.loading = loading;
          state.loadingHistory.push(loading);
        },
        setSelection: (selection) => (state.selection = selection),
        setError: (error) => (state.error = error),
        clearSelectionId: () => {
          state.selectionId = null;
          const stored = loadCheckoutDraft('cart_1', storage);
          if (stored) saveCheckoutDraft('cart_1', clearCvsSelection(stored), storage);
        },
      });
    return { storage, state, run };
  }

  it('讀到 → 顯示門市，讀取中狀態有開有關', async () => {
    const p = page(() => Promise.resolve(SELECTION));
    await p.run();
    expect(p.state.selection).toEqual(SELECTION);
    expect(p.state.error).toBeNull();
    expect(p.state.loadingHistory).toEqual([true, false]);
  });

  it('404 → 清票（state 與草稿）、留言還在、顯示逾時那句', async () => {
    const p = page(() => Promise.reject(apiError(404, 'platform.not-found')));
    await p.run();
    expect(p.state.selectionId).toBeNull();
    expect(p.state.selection).toBeNull();
    expect(p.state.error).toBe('門市選擇已逾時，請重新選擇門市。');
    expect(loadCheckoutDraft('cart_1', p.storage)).toEqual({ ...DRAFT, convenienceStoreSelectionId: null });
    expect(p.state.loading).toBe(false);
  });

  it.each([
    ['500', () => apiError(500, 'platform.unexpected')],
    ['網路中斷', () => new NetworkError('網路連線中斷')],
  ])('其他錯誤（%s）→ 票保留、顯示「暫時無法取得」', async (_label, makeError) => {
    const p = page(() => Promise.reject(makeError()));
    await p.run();
    expect(p.state.selectionId).toBe(NEW_TICKET);
    expect(loadCheckoutDraft('cart_1', p.storage)?.convenienceStoreSelectionId).toBe(NEW_TICKET);
    expect(p.state.error).toBe('暫時無法取得門市資料，請稍後再試。');
    expect(p.state.loading).toBe(false);
  });

  it('過期的請求回來時不動 state', async () => {
    const p = page(() => Promise.reject(apiError(404, 'platform.not-found')));
    await p.run(() => false);
    expect(p.state.selectionId).toBe(NEW_TICKET);
    expect(p.state.error).toBeNull();
  });
});

// ── 送出訂單 ─────────────────────────────────────────────────────────────

describe('checkoutStoreSelectionId：只有超商取貨才送票', () => {
  it('超商取貨送票；宅配、自取、未選都送 null', () => {
    expect(checkoutStoreSelectionId('ConvenienceStore', NEW_TICKET)).toBe(NEW_TICKET);
    expect(checkoutStoreSelectionId('HomeDelivery', NEW_TICKET)).toBeNull();
    expect(checkoutStoreSelectionId('SelfPickup', NEW_TICKET)).toBeNull();
    expect(checkoutStoreSelectionId(null, NEW_TICKET)).toBeNull();
  });
});

describe('handleCheckoutFailure：送出訂單失敗的分支', () => {
  function callbacks() {
    return {
      onUnauthorized: vi.fn(),
      clearSelectionId: vi.fn(),
      setStoreError: vi.fn(),
      setSubmitError: vi.fn(),
      setSubmitting: vi.fn(),
    };
  }

  it('422 checkout.store-selection-expired → 清票、逾時那句、放開 submitting', () => {
    const cb = callbacks();
    handleCheckoutFailure(apiError(422, 'checkout.store-selection-expired'), cb);
    expect(cb.clearSelectionId).toHaveBeenCalledTimes(1);
    expect(cb.setStoreError).toHaveBeenCalledWith('門市選擇已逾時，請重新選擇門市。');
    expect(cb.setSubmitting).toHaveBeenCalledWith(false);
    expect(cb.setSubmitError).not.toHaveBeenCalled();
    expect(cb.onUnauthorized).not.toHaveBeenCalled();
  });

  it('401 → 導去登入，不放 submitting、不清票', () => {
    const cb = callbacks();
    handleCheckoutFailure(apiError(401, 'identity.unauthenticated'), cb);
    expect(cb.onUnauthorized).toHaveBeenCalledTimes(1);
    expect(cb.setSubmitting).not.toHaveBeenCalled();
    expect(cb.clearSelectionId).not.toHaveBeenCalled();
  });

  it('其他 422 → 顯示後端的訊息、放開 submitting、票不動', () => {
    const cb = callbacks();
    handleCheckoutFailure(apiError(422, 'checkout.address-required'), cb);
    expect(cb.setSubmitError).toHaveBeenCalledWith({ title: '後端的 checkout.address-required', traceId: null });
    expect(cb.setSubmitting).toHaveBeenCalledWith(false);
    expect(cb.clearSelectionId).not.toHaveBeenCalled();
  });
});
