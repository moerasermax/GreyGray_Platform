'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { listCategories } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from '../_lib/apiClient';
import { MOBILE_NAV_TEXT } from '../_lib/mobileNav';

type Categories = Awaited<ReturnType<typeof listCategories>>;

export interface DrawerCategoriesProps {
  open: boolean;
  onNavigate: () => void;
}

/** 第一次打開抽屜才取分類；成功後在這個抽屜實例中重用。 */
export function DrawerCategories({ open, onNavigate }: DrawerCategoriesProps) {
  const requestedRef = useRef(false);
  const mountedRef = useRef(false);
  const [state, setState] = useState<
    | { status: 'idle' | 'loading' | 'failed' }
    | { status: 'ready'; categories: Categories }
  >({ status: 'idle' });

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    if (!open || requestedRef.current) return;
    requestedRef.current = true;
    setState({ status: 'loading' });

    listCategories(browserApi())
      .then((categories) => {
        if (mountedRef.current) setState({ status: 'ready', categories });
      })
      .catch(() => {
        if (mountedRef.current) setState({ status: 'failed' });
      });
  }, [open]);

  if (state.status === 'idle' || state.status === 'failed') return null;

  if (state.status === 'loading') {
    return (
      <section aria-labelledby="drawer-categories-title">
        <h3
          id="drawer-categories-title"
          className="text-[length:var(--gg-text-sm)] font-extrabold text-fg"
        >
          {MOBILE_NAV_TEXT.categoriesTitle}
        </h3>
        <div
          aria-hidden="true"
          className="mt-[var(--gg-space-2)] h-[var(--gg-space-8)] rounded-[var(--gg-radius-md)] bg-surface-sunken"
        />
      </section>
    );
  }

  if (state.status !== 'ready' || state.categories.length === 0) return null;

  return (
    <section aria-labelledby="drawer-categories-title">
      <h3
        id="drawer-categories-title"
        className="text-[length:var(--gg-text-sm)] font-extrabold text-fg"
      >
        {MOBILE_NAV_TEXT.categoriesTitle}
      </h3>
      <ul className="mt-[var(--gg-space-2)]">
        {state.categories.map((category) => (
          <li key={category.id}>
            <Link
              href={`/categories/${category.id}`}
              onClick={onNavigate}
              className="flex min-h-[var(--gg-touch-min)] items-center rounded-[var(--gg-radius-sm)] px-[var(--gg-space-3)] font-bold text-fg no-underline hover:bg-surface-sunken"
            >
              {category.name}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
