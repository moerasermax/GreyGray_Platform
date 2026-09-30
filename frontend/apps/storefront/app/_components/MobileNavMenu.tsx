'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { IconMenu, NavDrawer } from '@greygray/ui';
import {
  MOBILE_NAV_DRAWER_ID,
  MOBILE_NAV_GROUPS,
  MOBILE_NAV_TEXT,
  isMobileNavLinkCurrent,
} from '../_lib/mobileNav';
import { DrawerCategories } from './DrawerCategories';

const LINK_CLASS =
  'flex min-h-[var(--gg-touch-min)] items-center rounded-[var(--gg-radius-sm)] px-[var(--gg-space-3)] font-bold text-fg no-underline hover:bg-surface-sunken';

export function MobileNavMenu() {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const closeMenu = useCallback(() => setOpen(false), []);

  useEffect(() => closeMenu(), [pathname, closeMenu]);

  useEffect(() => {
    const desktop = window.matchMedia('(min-width: 1024px)');
    const closeAtDesktop = (event: MediaQueryListEvent) => {
      if (event.matches) closeMenu();
    };
    if (desktop.matches) closeMenu();
    desktop.addEventListener('change', closeAtDesktop);
    return () => desktop.removeEventListener('change', closeAtDesktop);
  }, [closeMenu]);

  const browseGroup = MOBILE_NAV_GROUPS[1];
  const infoGroup = MOBILE_NAV_GROUPS[2];

  return (
    <>
      <button
        type="button"
        aria-label={MOBILE_NAV_TEXT.openLabel}
        aria-expanded={open}
        aria-controls={MOBILE_NAV_DRAWER_ID}
        onClick={() => setOpen(true)}
        className="inline-flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] shrink-0 items-center justify-center rounded-pill text-[length:var(--gg-text-lg)] text-fg hover:bg-surface-sunken"
      >
        <IconMenu />
      </button>

      <NavDrawer
        open={open}
        onClose={closeMenu}
        id={MOBILE_NAV_DRAWER_ID}
        title={MOBILE_NAV_TEXT.drawerTitle}
        closeLabel={MOBILE_NAV_TEXT.closeLabel}
      >
        <nav aria-label={MOBILE_NAV_TEXT.drawerTitle}>
          <div className="space-y-[var(--gg-space-6)]">
            <DrawerCategories open={open} onNavigate={closeMenu} />
            {[browseGroup, infoGroup].map((group) => (
              <section key={group.title} aria-labelledby={`mobile-nav-${group.title}`}>
                <h3
                  id={`mobile-nav-${group.title}`}
                  className="text-[length:var(--gg-text-sm)] font-extrabold text-fg"
                >
                  {group.title}
                </h3>
                <ul className="mt-[var(--gg-space-2)]">
                  {group.links.map((link) => {
                    const current = isMobileNavLinkCurrent(pathname, link.href);
                    return (
                      <li key={link.href}>
                        <Link
                          href={link.href}
                          onClick={closeMenu}
                          aria-current={current ? 'page' : undefined}
                          className={LINK_CLASS}
                        >
                          {link.label}
                        </Link>
                      </li>
                    );
                  })}
                </ul>
              </section>
            ))}
          </div>
        </nav>
      </NavDrawer>
    </>
  );
}
