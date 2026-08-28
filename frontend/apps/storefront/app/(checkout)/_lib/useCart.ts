'use client';

import { useEffect, useState } from 'react';
import * as api from '@greygray/api-client/endpoints/storefront';
import type { components } from '@greygray/api-client/storefront';
import { browserApi } from '../../_lib/apiClient';
import { describeError, type ErrorDisplay } from './errorDisplay';

type S = components['schemas'];

export interface UseCartResult {
  readonly cart: S['Cart'] | null;
  readonly loading: boolean;
  readonly error: ErrorDisplay | null;
  /** 重新整理購物車（例如網路中斷之後按重試）。 */
  reload(): void;
  /** 改數量／刪除之類的操作已經回傳最新的 `Cart`，直接塞進去，不用整包重打。 */
  setCart(cart: S['Cart']): void;
}

/** 購物車頁與結帳頁共用的載入邏輯。集中在一處，兩邊的載入／錯誤處理才會一致。 */
export function useCart(): UseCartResult {
  const [cart, setCartState] = useState<S['Cart'] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ErrorDisplay | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    api
      .getCart(browserApi())
      .then((result) => {
        if (!cancelled) setCartState(result);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(describeError(cause));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  return {
    cart,
    loading,
    error,
    reload: () => setReloadToken((t) => t + 1),
    setCart: setCartState,
  };
}
