'use client';

import { useEffect, useRef, useState, type RefObject } from 'react';
import { Button, Skeleton } from '@greygray/ui';
import type { components } from '@greygray/api-client/storefront';
import { cvsMapFormFields, executeCvsMapStart, resetCvsMapStartOnPageShow } from '../_lib/cvsSelection';

type S = components['schemas'];

export interface ConvenienceStoreFieldProps {
  /** 讀票讀到的門市。沒選、讀取中、讀失敗都是 `null`。 */
  selection: S['CvsStoreSelection'] | null;
  loading: boolean;
  error: string | null;
  /** 頁面負責：存完整草稿、開選店票，回傳地圖導轉參數。 */
  onStart: () => Promise<S['CvsMapSession']>;
}

/**
 * 超商取貨選門市：7-ELEVEN 電子地圖（ADR-038）。
 *
 * 按下去開一張選店票，拿到的 `fields` 用隱藏表單**同一個分頁頂層**送到綠界地圖——
 * 手機不可放 iframe、iOS 不要開新視窗。選好之後後端把客人導回 `/checkout?cvsSelection=<票>`，
 * 讀票、404／422 的處理都是頁面的事（`_lib/cvsSelection.ts`）。
 *
 * 這個元件只管：同步鎖、拿到的地圖參數、隱藏表單與自動送出、開票失敗的訊息，
 * 以及客人從地圖按「上一頁」、瀏覽器從 bfcache 還原時放鎖。
 */
export function ConvenienceStoreField({ selection, loading, error, onStart }: ConvenienceStoreFieldProps) {
  const pendingRef = useRef(false);
  const formRef = useRef<HTMLFormElement>(null);
  const [starting, setStarting] = useState(false);
  const [session, setSession] = useState<S['CvsMapSession'] | null>(null);
  const [startError, setStartError] = useState<string | null>(null);

  useEffect(() => {
    if (session) formRef.current?.submit();
  }, [session]);

  useEffect(() => {
    function handlePageShow(event: PageTransitionEvent) {
      resetCvsMapStartOnPageShow(event, pendingRef, () => {
        setSession(null);
        setStarting(false);
      });
    }
    window.addEventListener('pageshow', handlePageShow);
    return () => window.removeEventListener('pageshow', handlePageShow);
  }, []);

  function handleStart() {
    void executeCvsMapStart({
      pendingRef,
      start: onStart,
      onStarting: () => {
        setStartError(null);
        setStarting(true);
      },
      onSession: setSession,
      onError: (message) => {
        setStarting(false);
        setStartError(message);
      },
    });
  }

  return (
    <ConvenienceStoreFieldView
      selection={selection}
      loading={loading}
      error={startError ?? error}
      starting={starting}
      session={session}
      formRef={formRef}
      onStart={handleStart}
    />
  );
}

export interface ConvenienceStoreFieldViewProps {
  selection: S['CvsStoreSelection'] | null;
  loading: boolean;
  error: string | null;
  starting: boolean;
  session: S['CvsMapSession'] | null;
  formRef?: RefObject<HTMLFormElement | null> | undefined;
  onStart?: (() => void) | undefined;
}

/** 純呈現：畫什麼完全由 props 決定（測試用 `renderToStaticMarkup`）。 */
export function ConvenienceStoreFieldView({
  selection,
  loading,
  error,
  starting,
  session,
  formRef,
  onStart,
}: ConvenienceStoreFieldViewProps) {
  return (
    <div className="flex flex-col gap-[var(--gg-space-3)]">
      <p className="text-[length:var(--gg-text-sm)] font-bold text-fg-muted">取貨門市</p>

      {loading ? (
        <div className="flex flex-col gap-[var(--gg-space-2)]">
          <Skeleton variant="text" className="h-5 w-40" />
          <Skeleton variant="text" className="h-4 w-56" />
        </div>
      ) : selection ? (
        <div className="flex flex-col gap-[var(--gg-space-1)]">
          <p className="font-bold text-fg">{selection.storeName}</p>
          <p className="text-[length:var(--gg-text-sm)] text-fg-muted">{selection.storeAddress}</p>
          {selection.isOutlying && (
            <p className="text-[length:var(--gg-text-xs)] font-bold text-fg-muted">離島門市</p>
          )}
        </div>
      ) : (
        <p className="text-[length:var(--gg-text-sm)] text-fg-muted">
          會前往 7-ELEVEN 電子地圖，選好後自動回到這一頁
        </p>
      )}

      {error && (
        <p role="alert" className="text-[length:var(--gg-text-sm)] text-danger">
          {error}
        </p>
      )}

      <Button variant={selection ? 'secondary' : 'primary'} loading={starting} onClick={onStart}>
        {selection ? '重新選擇' : '選擇 7-ELEVEN 門市'}
      </Button>

      {session && <CvsMapForm session={session} formRef={formRef} />}
    </div>
  );
}

export interface CvsMapFormProps {
  session: S['CvsMapSession'];
  formRef?: RefObject<HTMLFormElement | null> | undefined;
}

/**
 * 地圖導轉表單。**`fields` 原封不動**：不排序、不過濾、不改值（照付款頁的做法）。
 * 自動送出由 {@link ConvenienceStoreField} 的 effect 做；沒跳走時給一顆手動的。
 */
export function CvsMapForm({ session, formRef }: CvsMapFormProps) {
  return (
    <>
      <form ref={formRef} method={session.method} action={session.action} className="hidden">
        {cvsMapFormFields(session).map((field) => (
          <input key={field.name} type="hidden" name={field.name} value={field.value} />
        ))}
      </form>
      <Button variant="secondary" onClick={() => formRef?.current?.submit()}>
        如果沒有自動跳轉，請點這裡
      </Button>
    </>
  );
}
