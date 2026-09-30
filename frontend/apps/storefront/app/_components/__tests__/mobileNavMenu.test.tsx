import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { INFO_LINKS } from '../../(info)/_components/InfoLinks';
import {
  navDrawerLockScroll,
  navDrawerRestoreScroll,
} from '@greygray/ui';
import {
  MOBILE_NAV_DRAWER_ID,
  MOBILE_NAV_GROUPS,
  MOBILE_NAV_TEXT,
  isMobileNavLinkCurrent,
} from '../../_lib/mobileNav';
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
