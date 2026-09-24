'use client';

import { useCallback, useState, type ComponentType, type ReactNode } from 'react';
import { Drawer } from './Drawer';
import { LogoutIcon, MoonIcon, SunIcon } from './icons';

/**
 * Sidebar／Topbar 不直接依賴 `next/link`——`packages/ui` 不掛 Next.js 的依賴。
 * 呼叫端（`apps/admin`）把 `next/link` 的 `Link` 當 `LinkComponent` 傳進來，
 * `active` 狀態也由呼叫端用 `usePathname()` 算好再傳進來。
 */
export type NavLinkComponent = ComponentType<{
  href: string;
  className?: string;
  children?: ReactNode;
  onClick?: () => void;
}>;

export interface NavItem {
  readonly key: string;
  readonly label: string;
  readonly href: string;
  readonly icon: ReactNode;
  readonly active?: boolean;
}

export interface SidebarProps {
  readonly items: readonly NavItem[];
  readonly LinkComponent: NavLinkComponent;
  readonly header?: ReactNode;
}

interface NavListProps {
  readonly items: readonly NavItem[];
  readonly LinkComponent: NavLinkComponent;
  /** 手機抽屜用：點任一項後關閉抽屜。桌面側邊欄不傳。 */
  readonly onNavigate?: () => void;
  /** 手機抽屜用：項目高度撐到 44px 觸控區。 */
  readonly touch?: boolean;
}

/** 側邊欄與手機抽屜共用同一份項目清單，呼叫端已依角色篩過，這裡不再判斷。 */
function NavList({ items, LinkComponent, onNavigate, touch }: NavListProps) {
  return (
    <ul className="flex flex-col gap-1">
      {items.map((item) => (
        <li key={item.key}>
          <LinkComponent
            href={item.href}
            {...(onNavigate ? { onClick: onNavigate } : {})}
            className={`flex items-center gap-3 rounded-sm px-3 text-sm font-medium transition-colors ${
              touch ? 'min-h-11 py-2.5' : 'py-2'
            } ${
              item.active
                ? 'bg-primary-subtle text-primary-text'
                : 'text-fg-muted hover:bg-surface-sunken hover:text-fg'
            }`}
          >
            {item.icon}
            <span>{item.label}</span>
          </LinkComponent>
        </li>
      ))}
    </ul>
  );
}

export function Sidebar({ items, LinkComponent, header }: SidebarProps) {
  return (
    <aside
      className="hidden shrink-0 flex-col border-r border-border-soft bg-surface md:flex"
      style={{ width: 'var(--ga-sidebar-width)' }}
    >
      {header ? (
        <div
          className="flex items-center border-b border-border-soft px-5"
          style={{ height: 'var(--ga-topbar-height)' }}
        >
          {header}
        </div>
      ) : null}
      <nav aria-label="主選單" className="flex-1 overflow-y-auto p-3">
        <NavList items={items} LinkComponent={LinkComponent} />
      </nav>
    </aside>
  );
}

/** 手機導覽：md 以下由 Topbar 的「選單」按鈕開啟，項目與 Sidebar 完全相同。 */
export interface MobileNavProps {
  readonly items: readonly NavItem[];
  readonly LinkComponent: NavLinkComponent;
}

const MOBILE_NAV_ID = 'gg-admin-mobile-nav';

export interface TopbarProps {
  readonly title?: ReactNode;
  readonly staffName: string;
  readonly staffRoleLabel: string;
  readonly theme: 'light' | 'dark';
  readonly onToggleTheme: () => void;
  readonly onLogout: () => void;
  /** 有傳才會在 md 以下出現「選單」按鈕與抽屜；不傳時 Topbar 與過去完全相同。 */
  readonly mobileNav?: MobileNavProps;
}

export function Topbar({
  title,
  staffName,
  staffRoleLabel,
  theme,
  onToggleTheme,
  onLogout,
  mobileNav,
}: TopbarProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  // 傳給 Drawer／useFocusTrap 的 onClose 要穩定，否則每次 render 都會重跑 focus trap。
  const closeMenu = useCallback(() => setMenuOpen(false), []);

  return (
    <header
      className="flex items-center justify-between border-b border-border-soft bg-surface px-5"
      style={{ height: 'var(--ga-topbar-height)' }}
    >
      <div className="flex min-w-0 items-center gap-2">
        {mobileNav ? (
          <button
            type="button"
            onClick={() => setMenuOpen(true)}
            aria-label="開啟選單"
            aria-expanded={menuOpen}
            aria-controls={MOBILE_NAV_ID}
            className="-ml-2 flex h-11 w-11 shrink-0 items-center justify-center rounded-sm text-fg-muted hover:bg-surface-sunken hover:text-fg md:hidden"
          >
            <MenuIcon />
          </button>
        ) : null}
        <div className="truncate text-sm font-semibold text-fg">{title}</div>
      </div>
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={onToggleTheme}
          aria-label={theme === 'dark' ? '切換為淺色模式' : '切換為深色模式'}
          className="flex h-9 w-9 items-center justify-center rounded-sm text-fg-muted hover:bg-surface-sunken hover:text-fg"
        >
          {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
        </button>
        <div className="flex flex-col items-end leading-tight">
          <span className="text-sm font-medium text-fg">{staffName}</span>
          <span className="text-xs text-fg-muted">{staffRoleLabel}</span>
        </div>
        <button
          type="button"
          onClick={onLogout}
          aria-label="登出"
          className="flex h-9 w-9 items-center justify-center rounded-sm text-fg-muted hover:bg-danger-subtle hover:text-danger"
        >
          <LogoutIcon />
        </button>
      </div>

      {mobileNav ? (
        // 外層 md:hidden：視窗從手機寬拉到桌面寬時，還開著的抽屜跟著收起，桌面外觀不受影響。
        <div className="md:hidden">
          <Drawer open={menuOpen} onClose={closeMenu} title="選單">
            <nav id={MOBILE_NAV_ID} aria-label="主選單">
              <NavList
                items={mobileNav.items}
                LinkComponent={mobileNav.LinkComponent}
                onNavigate={closeMenu}
                touch
              />
            </nav>
          </Drawer>
        </div>
      ) : null}
    </header>
  );
}

/** 漢堡選單圖示。`icons.tsx` 不在本包所有權內，所以放這裡；尺寸與其他 icon 一致（20）。 */
function MenuIcon() {
  return (
    <svg
      width={20}
      height={20}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M4 7h16" />
      <path d="M4 12h16" />
      <path d="M4 17h16" />
    </svg>
  );
}
