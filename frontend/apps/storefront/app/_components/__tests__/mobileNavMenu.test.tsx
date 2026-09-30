import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { INFO_LINKS } from '../../(info)/_components/InfoLinks';
import {
  navDrawerLockScroll,
  navDrawerRestoreScroll,
} from '@greygray/ui';
import {
  MOBILE_NAV_DRAWER_ID,
  MOBILE_NAV_DESKTOP_QUERY,
  MOBILE_NAV_GROUPS,
  MOBILE_NAV_TEXT,
  isMobileNavLinkCurrent,
  subscribeMobileNavDesktop,
} from '../../_lib/mobileNav';
import { createDrawerCategoriesCache } from '../DrawerCategories';
import { MobileNavMenu } from '../MobileNavMenu';

(globalThis as unknown as { React: typeof React }).React = React;

describe('MobileNavMenu 的 SSR 觸發鈕', () => {
  it('pathname 為 null 仍可渲染完整的關閉狀態語意', () => {
    const html = renderToStaticMarkup(<MobileNavMenu />);
    expect(html).toContain('type="button"');
    expect(html).toContain(`aria-label="${MOBILE_NAV_TEXT.openLabel}"`);
    expect(html).toContain('aria-expanded="false"');
    expect(html).toContain(`aria-controls="${MOBILE_NAV_DRAWER_ID}"`);
  });
});

describe('NavDrawer 的 html 捲動鎖純函式', () => {
  it('鎖定設 hidden，還原為原本的 scroll', () => {
    const target = { style: { overflow: 'scroll' } };
    const previous = navDrawerLockScroll(target);
    expect(target.style.overflow).toBe('hidden');
    navDrawerRestoreScroll(target, previous);
    expect(target.style.overflow).toBe('scroll');
  });
});

describe('導覽分組與目前頁比對', () => {
  it('三組順序固定，購物指南直接重用 INFO_LINKS', () => {
    expect(MOBILE_NAV_GROUPS.map((group) => group.title)).toEqual([
      '商品分類',
      '逛逛',
      '購物指南',
    ]);
    expect(MOBILE_NAV_GROUPS[2].links).toBe(INFO_LINKS);
  });

  it('/faq 只對完全相同的 /faq 標成 current', () => {
    expect(isMobileNavLinkCurrent('/faq', '/faq')).toBe(true);
    expect(isMobileNavLinkCurrent('/faq/x', '/faq')).toBe(false);
  });
});

describe('FE-54 導覽預取與桌面斷點', () => {
  it('T3：失敗清快取，重試成功後兩個呼叫端共用同一個請求', async () => {
    const cache = createDrawerCategoriesCache();
    const categories = [{ id: 'category-1', name: '分類一' }];
    const loader = vi
      .fn<() => Promise<typeof categories>>()
      .mockRejectedValueOnce(new Error('temporary'))
      .mockResolvedValueOnce(categories);

    await expect(cache.load(loader)).rejects.toThrow('temporary');
    const first = cache.load(loader);
    const second = cache.load(loader);
    expect(first).toBe(second);
    await expect(first).resolves.toBe(categories);
    expect(loader).toHaveBeenCalledTimes(2);
  });

  it('T4：64rem 斷點在舊 Safari 以 addListener 註冊並用 removeListener 卸載', () => {
    expect(MOBILE_NAV_DESKTOP_QUERY).toBe('(min-width: 64rem)');
    const addListener = vi.fn();
    const removeListener = vi.fn();
    const listener = vi.fn();
    const unsubscribe = subscribeMobileNavDesktop(
      { matches: false, addListener, removeListener },
      listener,
    );

    expect(addListener).toHaveBeenCalledWith(listener);
    unsubscribe();
    expect(removeListener).toHaveBeenCalledWith(listener);
  });
});
