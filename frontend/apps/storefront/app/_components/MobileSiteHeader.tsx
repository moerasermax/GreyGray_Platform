'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { shouldShowMobileSiteHeader } from '../_lib/mobileNav';
import { MobileNavMenu } from './MobileNavMenu';

export function MobileSiteHeader() {
  const pathname = usePathname();
  if (!shouldShowMobileSiteHeader(pathname)) return null;

  return (
    <header
      className="sticky top-0 z-[var(--gg-z-header)] grid grid-cols-[auto_1fr_auto] items-center border-b border-border bg-surface px-[var(--gg-space-4)] lg:hidden"
      style={{
        minHeight: 'calc(var(--gg-top-bar-height) + env(safe-area-inset-top, 0px))',
        paddingTop: 'env(safe-area-inset-top, 0px)',
      }}
    >
      <MobileNavMenu />
      <Link
        href="/"
        className="justify-self-center rounded-[var(--gg-radius-sm)] font-display text-[length:var(--gg-text-xl)] font-extrabold text-fg no-underline"
      >
        GreyGray
      </Link>
      <span aria-hidden="true" className="h-[var(--gg-touch-min)] w-[var(--gg-touch-min)]" />
    </header>
  );
}
