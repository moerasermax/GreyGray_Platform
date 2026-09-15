import { ApiError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { loginHref } from '../../_lib/auth';

type Product = components['schemas']['ProductListItem'];

export type FavoriteListView = 'loading' | 'error' | 'empty' | 'items';

export function favoriteListView(loading: boolean, error: unknown, items: readonly Product[]): FavoriteListView {
  if (loading) return 'loading';
  if (error != null) return 'error';
  return items.length === 0 ? 'empty' : 'items';
}

/** 重試或游標重疊時不重複畫同一張卡，原有順序保持不變。 */
export function appendFavoritePage(current: readonly Product[], incoming: readonly Product[]): Product[] {
  const seen = new Set(current.map((product) => product.id));
  return [...current, ...incoming.filter((product) => !seen.has(product.id))];
}

export function canLoadMore(nextCursor: string | null, pending: boolean): nextCursor is string {
  return nextCursor !== null && !pending;
}

/** 只有最愛列表自己的 401 才回登入網址；其他錯誤留在頁面上顯示。 */
export function favoriteListLoginHref(error: unknown): string | null {
  return error instanceof ApiError && error.isUnauthorized ? loginHref('/favorites') : null;
}
