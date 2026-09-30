'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { listCategories } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from '../_lib/apiClient';
import { MOBILE_NAV_TEXT } from '../_lib/mobileNav';

type Categories = Awaited<ReturnType<typeof listCategories>>;

export interface DrawerCategoriesProps {
  open: boolean;
  onNavigate: () => void;
}

export function createDrawerCategoriesCache() {
  let request: Promise<Categories> | null = null;

  return {
    load(loader: () => Promise<Categories>): Promise<Categories> {
      if (request) return request;
      request = Promise.resolve()
        .then(loader)
        .catch((error: unknown) => {
          request = null;
          throw error;
        });
      return request;
    },
  };
}

const categoriesCache = createDrawerCategoriesCache();

/** 掛載時預取分類；成功結果由頁面載入期間的所有抽屜實例共用。 */
export function DrawerCategories({ open, onNavigate }: DrawerCategoriesProps) {
  const mountedRef = useRef(false);
  const previousOpenRef = useRef(open);
  const [state, setState] = useState<
    | { status: 'idle' | 'loading' | 'failed' }
    | { status: 'ready'; categories: Categories }
  >({ status: 'loading' });

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const load = useCallback(() => {
    setState({ status: 'loading' });

    void categoriesCache
      .load(() => listCategories(browserApi()))
      .then((categories) => {
        if (mountedRef.current) setState({ status: 'ready', categories });
      })
      .catch(() => {
        if (mountedRef.current) setState({ status: 'failed' });
      });
  }, []);

  useEffect(load, [load]);

  useEffect(() => {
    const opened = open && !previousOpenRef.current;
    previousOpenRef.current = open;
    if (opened && state.status === 'failed') load();
  }, [load, open, state.status]);

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
