'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { listCategories } from '@greygray/api-client/endpoints/storefront';
import { browserApi } from '../_lib/apiClient';
import { buildCategoryTree, type CategoryTreeNode } from '../_lib/categoryTree';
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

export interface DrawerCategoryTreeProps {
  readonly tree: readonly CategoryTreeNode[];
  readonly onNavigate: () => void;
}

export function DrawerCategoryTree({ tree, onNavigate }: DrawerCategoryTreeProps) {
  const [expandedIds, setExpandedIds] = useState<ReadonlySet<string>>(() => new Set());

  return (
    <ul className="mt-[var(--gg-space-2)]">
      {tree.map(({ root, children }) => {
        const expanded = expandedIds.has(root.id);
        const childrenId = `drawer-category-${root.id}-children`;
        return (
          <li key={root.id}>
            <div className="flex min-h-[var(--gg-touch-min)] items-stretch">
              <Link
                href={`/categories/${root.id}`}
                onClick={onNavigate}
                className="flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] flex-1 items-center rounded-[var(--gg-radius-sm)] px-[var(--gg-space-3)] font-bold text-fg no-underline hover:bg-surface-sunken"
              >
                {root.name}
              </Link>
              {children.length > 0 ? (
                <button
                  type="button"
                  aria-expanded={expanded}
                  aria-controls={childrenId}
                  aria-label={MOBILE_NAV_TEXT.categoryToggleLabel(root.name, expanded)}
                  className="flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] items-center justify-center rounded-[var(--gg-radius-sm)] font-bold text-fg hover:bg-surface-sunken"
                  onClick={() => {
                    setExpandedIds((current) => {
                      const next = new Set(current);
                      if (next.has(root.id)) next.delete(root.id);
                      else next.add(root.id);
                      return next;
                    });
                  }}
                >
                  <span aria-hidden="true">{expanded ? '−' : '+'}</span>
                </button>
              ) : null}
            </div>
            {children.length > 0 ? (
              <ul id={childrenId} hidden={!expanded} className="pl-[var(--gg-space-4)]">
                {children.map((child) => (
                  <li key={child.id}>
                    <Link
                      href={`/categories/${child.id}`}
                      onClick={onNavigate}
                      className="flex min-h-[var(--gg-touch-min)] min-w-[var(--gg-touch-min)] items-center rounded-[var(--gg-radius-sm)] px-[var(--gg-space-3)] font-bold text-fg no-underline hover:bg-surface-sunken"
                    >
                      {child.name}
                    </Link>
                  </li>
                ))}
              </ul>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

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
      <DrawerCategoryTree tree={buildCategoryTree(state.categories)} onNavigate={onNavigate} />
    </section>
  );
}
