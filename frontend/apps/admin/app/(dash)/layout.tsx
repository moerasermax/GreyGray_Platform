'use client';

import {
  CatalogIcon,
  CampaignIcon,
  DashboardIcon,
  Dialog,
  LedgerIcon,
  OrderIcon,
  Sidebar,
  Topbar,
  ToastProvider,
  type NavItem,
} from '@greygray/ui/admin';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useState, type ReactNode } from 'react';
import {
  getSession,
  hasRequiredRole,
  logout,
  roleLabel,
  type Staff,
  type StaffRole,
} from '../login/_lib/session';

const THEME_STORAGE_KEY = 'gg-admin-theme';
type Theme = 'light' | 'dark';

interface NavDefinition {
  readonly key: string;
  readonly label: string;
  readonly href: string;
  readonly requiredRole: StaffRole;
  readonly icon: ReactNode;
}

const NAV_DEFINITIONS: readonly NavDefinition[] = [
  { key: 'dashboard', label: '儀表板', href: '/', requiredRole: 'ReadOnly', icon: <DashboardIcon /> },
  { key: 'catalog', label: '商品管理', href: '/catalog', requiredRole: 'ReadOnly', icon: <CatalogIcon /> },
  { key: 'campaigns', label: '開團管理', href: '/campaigns', requiredRole: 'ReadOnly', icon: <CampaignIcon /> },
  { key: 'orders', label: '訂單', href: '/orders', requiredRole: 'ReadOnly', icon: <OrderIcon /> },
  {
    key: 'procurement',
    label: '現場採購',
    href: '/procurement',
    requiredRole: 'Operator',
    icon: (
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
        <path d="M3 7h18l-1.6 12.2a2 2 0 0 1-2 1.8H6.6a2 2 0 0 1-2-1.8L3 7Z" />
        <path d="M8 7V5.5a4 4 0 0 1 8 0V7" />
      </svg>
    ),
  },
  {
    key: 'shipments',
    label: '出貨',
    href: '/shipments',
    requiredRole: 'Operator',
    icon: (
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
        <path d="M3.5 8.5 12 4l8.5 4.5v7L12 20l-8.5-4.5v-7Z" />
        <path d="M3.5 8.5 12 13l8.5-4.5" />
        <path d="M12 13v7" />
      </svg>
    ),
  },
  { key: 'ledger', label: '帳務', href: '/ledger', requiredRole: 'Accountant', icon: <LedgerIcon /> },
];

function isActive(pathname: string, href: string): boolean {
  if (href === '/') return pathname === '/';
  return pathname === href || pathname.startsWith(`${href}/`);
}

export default function DashLayout({ children }: { readonly children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [staff, setStaff] = useState<Staff | null | undefined>(undefined);
  const [theme, setTheme] = useState<Theme>('light');
  const [logoutOpen, setLogoutOpen] = useState(false);

  useEffect(() => {
    const session = getSession();
    if (!session) {
      router.replace('/login');
      return;
    }
    setStaff(session);
  }, [router]);

  useEffect(() => {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') {
      setTheme(stored);
    }
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem(THEME_STORAGE_KEY, theme);
  }, [theme]);

  async function handleConfirmLogout() {
    await logout();
    setLogoutOpen(false);
    router.replace('/login');
  }

  // 還在確認 session 或已經被導去登入頁的路上——不要閃一下受保護的畫面再跳轉。
  if (!staff) {
    return <div className="min-h-screen bg-bg" />;
  }

  const navItems: readonly NavItem[] = NAV_DEFINITIONS.filter((item) =>
    hasRequiredRole(staff.role, item.requiredRole),
  ).map((item) => ({
    key: item.key,
    label: item.label,
    href: item.href,
    icon: item.icon,
    active: isActive(pathname, item.href),
  }));

  const pageTitle = navItems.find((item) => item.active)?.label ?? 'GreyGray 後台';

  return (
    <ToastProvider>
      <div className="flex min-h-screen bg-bg">
        <Sidebar
          items={navItems}
          LinkComponent={Link}
          header={<span className="text-base font-semibold text-primary">GreyGray 後台</span>}
        />
        <div className="flex min-w-0 flex-1 flex-col">
          <Topbar
            title={pageTitle}
            staffName={staff.displayName}
            staffRoleLabel={roleLabel(staff.role)}
            theme={theme}
            onToggleTheme={() => setTheme((current) => (current === 'dark' ? 'light' : 'dark'))}
            onLogout={() => setLogoutOpen(true)}
          />
          <main className="mx-auto w-full max-w-[var(--ga-container-max)] flex-1 p-6">{children}</main>
        </div>
      </div>

      <Dialog
        open={logoutOpen}
        onClose={() => setLogoutOpen(false)}
        title="確定要登出嗎？"
        description="登出後要重新輸入帳號密碼才能回到後台。"
        footer={
          <>
            <button
              type="button"
              onClick={() => setLogoutOpen(false)}
              className="rounded-full border border-border-strong px-4 py-1.5 text-sm font-medium text-fg hover:bg-surface-sunken"
            >
              取消
            </button>
            <button
              type="button"
              onClick={handleConfirmLogout}
              className="rounded-full border border-danger/30 bg-danger-subtle px-4 py-1.5 text-sm font-semibold text-danger hover:opacity-90"
            >
              登出
            </button>
          </>
        }
      />
    </ToastProvider>
  );
}
