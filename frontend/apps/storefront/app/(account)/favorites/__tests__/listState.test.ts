import { ApiError } from '@greygray/api-client';
import type { components } from '@greygray/api-client/storefront';
import { describe, expect, it } from 'vitest';
import { appendFavoritePage, canLoadMore, favoriteListLoginHref, favoriteListView } from '../listState';

type Product = components['schemas']['ProductListItem'];

function product(id: string): Product {
  return {
    id,
    name: id,
    imageUrl: null,
    priceFrom: null,
    badges: [],
    isFavorited: true,
  };
}

function apiError(status: number) {
  return new ApiError({
    type: 'https://greygray.tw/errors/test',
    title: '測試錯誤',
    status,
    code: 'test.error',
  });
}

describe('最愛列表狀態', () => {
  it('讀取失敗顯示 error，不會被當成空清單', () => {
    expect(favoriteListView(false, apiError(500), [])).toBe('error');
    expect(favoriteListView(false, null, [])).toBe('empty');
  });

  it('載入更多保留前頁順序，重疊項目不重複', () => {
    expect(appendFavoritePage([product('a'), product('b')], [product('b'), product('c')]).map((x) => x.id))
      .toEqual(['a', 'b', 'c']);
  });

  it('有下一頁且沒有請求進行中才允許載入更多；末頁不再送', () => {
    expect(canLoadMore('cursor-2', false)).toBe(true);
    expect(canLoadMore('cursor-2', true)).toBe(false);
    expect(canLoadMore(null, false)).toBe(false);
  });

  it('列表請求 401 才導登入，next 解碼後是 /favorites', () => {
    const href = favoriteListLoginHref(apiError(401));
    expect(href).not.toBeNull();
    expect(new URLSearchParams(href!.split('?')[1]).get('next')).toBe('/favorites');
    expect(favoriteListLoginHref(apiError(500))).toBeNull();
    expect(favoriteListLoginHref(new Error('offline'))).toBeNull();
  });
});
