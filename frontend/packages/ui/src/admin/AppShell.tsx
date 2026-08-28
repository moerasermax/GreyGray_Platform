import type { ComponentType, ReactNode } from 'react';
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
        <ul className="flex flex-col gap-1">
          {items.map((item) => (
            <li key={item.key}>
              <LinkComponent
                href={item.href}
                className={`flex items-center gap-3 rounded-sm px-3 py-2 text-sm font-medium transition-colors ${
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
      </nav>
    </aside>
  );
}

export interface TopbarProps {
  readonly title?: ReactNode;
  readonly staffName: string;
  readonly staffRoleLabel: string;
  readonly theme: 'light' | 'dark';
  readonly onToggleTheme: () => void;
  readonly onLogout: () => void;
}

export function Topbar({
  title,
  staffName,
  staffRoleLabel,
  theme,
  onToggleTheme,
  onLogout,
}: TopbarProps) {
  return (
    <header
      className="flex items-center justify-between border-b border-border-soft bg-surface px-5"
      style={{ height: 'var(--ga-topbar-height)' }}
    >
      <div className="text-sm font-semibold text-fg">{title}</div>
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
    </header>
  );
}
