'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Avatar, SearchBar } from '@greygray/ui';

/** 圓角搜尋列 ＋ 頭像（ADR-009 首頁切版第一段）。送出搜尋就導去商品列表頁。 */
export function HomeSearchHeader() {
  const router = useRouter();
  const [value, setValue] = useState('');

  function handleSubmit(keyword: string) {
    const trimmed = keyword.trim();
    router.push(trimmed ? `/products?q=${encodeURIComponent(trimmed)}` : '/products');
  }

  return (
    <div className="flex items-center gap-[var(--gg-space-3)]">
      <SearchBar value={value} onChange={setValue} onSubmit={handleSubmit} className="flex-1" />
      {/* 會員頁是 `(account)/me`——訂單、地址、儲值金與登出都從那裡進去。 */}
      <Link href="/me" aria-label="會員中心" className="shrink-0">
        <Avatar alt="會員中心" />
      </Link>
    </div>
  );
}
