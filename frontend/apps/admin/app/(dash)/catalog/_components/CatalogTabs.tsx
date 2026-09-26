'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';

const TABS = [
  { href: '/catalog', label: '商品' },
  { href: '/catalog/categories', label: '分類' },
] as const;

/**
 * 商品／分類的分頁式導覽。後台殼（FE-6）的側邊欄只到「商品管理」這一層，
 * 底下這兩頁的切換是 FE-7 自己的地盤，不動 `(dash)/layout.tsx`。
 */
export function CatalogTabs() {
  const pathname = usePathname();

  return (
    <div className="flex gap-1 border-b border-border-soft">
      {TABS.map((tab) => {
        const active = tab.href === '/catalog' ? pathname === '/catalog' : pathname.startsWith(tab.href);
        return (
          <Link
            key={tab.href}
            href={tab.href}
            className={`px-3 py-2 text-sm font-medium ${
              active ? 'border-b-2 border-primary-strong text-primary-text' : 'text-fg-muted hover:text-fg'
            }`}
          >
            {tab.label}
          </Link>
        );
      })}
    </div>
  );
}
