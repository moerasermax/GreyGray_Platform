import { INFO_LINKS } from '../(info)/_components/InfoLinks';
import { normalizePathname, shouldShowTabBar } from './tabs';

export const MOBILE_NAV_DRAWER_ID = 'mobile-navigation-drawer';

export const MOBILE_NAV_TEXT = {
  openLabel: '開啟選單',
  closeLabel: '關閉選單',
  drawerTitle: '網站導覽',
  categoriesTitle: '商品分類',
  browseTitle: '逛逛',
  infoTitle: '購物指南',
} as const;

export const MOBILE_NAV_BROWSE_LINKS = [
  { href: '/campaigns', label: '開團' },
  { href: '/products', label: '全部商品' },
] as const;

export const MOBILE_NAV_GROUPS = [
  { title: MOBILE_NAV_TEXT.categoriesTitle, links: [] },
  { title: MOBILE_NAV_TEXT.browseTitle, links: MOBILE_NAV_BROWSE_LINKS },
  { title: MOBILE_NAV_TEXT.infoTitle, links: INFO_LINKS },
] as const;

/** 分頁列那一側才補手機頁首；首頁已有自己的搜尋頁首。 */
export function shouldShowMobileSiteHeader(pathname: string | null): boolean {
  if (pathname === null) return false;
  return shouldShowTabBar(pathname) && normalizePathname(pathname) !== '/';
}

/** 導覽抽屜只標示完全相同的目的頁，不把子頁誤標成目前頁。 */
export function isMobileNavLinkCurrent(pathname: string | null, href: string): boolean {
  if (pathname === null) return false;
  return normalizePathname(pathname) === normalizePathname(href);
}
