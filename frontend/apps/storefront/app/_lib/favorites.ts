'use client';

import { useRef, useState } from 'react';
import {
  ApiError,
  createPayloadIdempotencyScope,
  type PayloadIdempotencyScope,
} from '@greygray/api-client';
import { addFavorite, removeFavorite } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from './apiClient';

export interface FavoriteMutationPayload {
  readonly productId: string;
  readonly desiredState: boolean;
}

export interface PendingRef {
  current: boolean;
}

interface ExecuteFavoriteToggleOptions {
  readonly productId: string;
  readonly currentFavorited: boolean;
  readonly pendingRef: PendingRef;
  readonly idempotency: PayloadIdempotencyScope;
  readonly mutate: (payload: FavoriteMutationPayload, idempotencyKey: string) => Promise<void>;
  readonly onChange: (favorited: boolean) => void;
  readonly onUnauthorized: () => void;
  readonly onError: (message: string) => void;
}

/**
 * 商品卡與商品頁共用的收藏狀態機。
 * `pendingRef` 在第一個 await 之前上鎖，React 尚未重繪時的同步連點也進不來。
 */
export async function executeFavoriteToggle(options: ExecuteFavoriteToggleOptions): Promise<void> {
  if (options.pendingRef.current) return;
  options.pendingRef.current = true;

  const previous = options.currentFavorited;
  const payload: FavoriteMutationPayload = {
    productId: options.productId,
    desiredState: !previous,
  };
  options.onChange(payload.desiredState);

  try {
    await options.mutate(payload, options.idempotency.current(payload));
    options.idempotency.complete();
  } catch (cause) {
    const unauthorized = cause instanceof ApiError && cause.isUnauthorized;
    // 未登入不能保留任何「好像已經收藏」的畫面狀態，導頁前一律回到未收藏。
    options.onChange(unauthorized ? false : previous);
    if (unauthorized) {
      options.onUnauthorized();
    } else {
      options.onError(
        cause instanceof ApiError ? cause.problem.title : '更新最愛失敗，請稍後再試。',
      );
    }
  } finally {
    options.pendingRef.current = false;
  }
}

export interface UseFavoriteToggleOptions {
  readonly productId: string;
  readonly initialFavorited: boolean;
  readonly onUnauthorized: () => void;
}

/** 共用 React 接線：樂觀更新、失敗還原、401 導頁與同商品同步防連點都只在這裡。 */
export function useFavoriteToggle(options: UseFavoriteToggleOptions) {
  const [favorited, setFavoritedState] = useState(options.initialFavorited);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const favoritedRef = useRef(options.initialFavorited);
  const pendingRef = useRef(false);
  const idempotencyRef = useRef<PayloadIdempotencyScope | null>(null);
  const latestOptionsRef = useRef(options);
  latestOptionsRef.current = options;
  idempotencyRef.current ??= createPayloadIdempotencyScope();

  function setFavorited(next: boolean) {
    favoritedRef.current = next;
    setFavoritedState(next);
  }

  async function toggle(): Promise<void> {
    setErrorMessage(null);
    await executeFavoriteToggle({
      productId: latestOptionsRef.current.productId,
      currentFavorited: favoritedRef.current,
      pendingRef,
      idempotency: idempotencyRef.current!,
      mutate: async (payload, idempotencyKey) => {
        if (payload.desiredState) {
          await addFavorite(browserApi(), payload.productId, { idempotencyKey });
        } else {
          await removeFavorite(browserApi(), payload.productId, { idempotencyKey });
        }
      },
      onChange: setFavorited,
      onUnauthorized: () => latestOptionsRef.current.onUnauthorized(),
      onError: setErrorMessage,
    });
  }

  return {
    favorited,
    errorMessage,
    toggle,
    clearError: () => setErrorMessage(null),
  };
}
