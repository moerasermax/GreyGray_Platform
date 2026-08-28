'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { SearchBar, Tabs, type TabItem } from '@greygray/ui';

const MODE_TABS: TabItem[] = [
  { value: 'all', label: '全部' },
  { value: 'Stock', label: '現貨' },
  { value: 'Preorder', label: '預購' },
];

export interface ProductFiltersProps {
  initialQuery: string;
  /** `'all'` 代表不帶 `mode` 篩選。 */
  initialMode: string;
}

/** 搜尋字與模式篩選都反映在網址上，重新整理或分享連結都能回到同一個結果。 */
export function ProductFilters({ initialQuery, initialMode }: ProductFiltersProps) {
  const router = useRouter();
  const [value, setValue] = useState(initialQuery);

  useEffect(() => setValue(initialQuery), [initialQuery]);

  function navigate(next: { q?: string; mode?: string }) {
    const q = next.q ?? initialQuery;
    const mode = next.mode ?? initialMode;
    const params = new URLSearchParams();
    if (q) params.set('q', q);
    if (mode !== 'all') params.set('mode', mode);
    const qs = params.toString();
    router.push(qs ? `/products?${qs}` : '/products');
  }

  return (
    <div className="flex flex-col gap-[var(--gg-space-3)]">
      <SearchBar
        value={value}
        onChange={setValue}
        onSubmit={(keyword) => navigate({ q: keyword })}
        placeholder="搜尋商品"
      />
      <Tabs items={MODE_TABS} value={initialMode} onChange={(mode) => navigate({ mode })} />
    </div>
  );
}
