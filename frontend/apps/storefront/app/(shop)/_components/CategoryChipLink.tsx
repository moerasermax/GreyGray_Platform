'use client';

import { useRouter } from 'next/navigation';
import { CategoryChip } from '@greygray/ui';

export interface CategoryChipLinkProps {
  categoryId: string;
  imageSrc?: string | null | undefined;
  imageAlt: string;
  label: string;
}

/**
 * `CategoryChip` 本身是一個 `<button>`；用 `next/link` 包住會變成 `<a><button>` 的
 * 巢狀互動元素，不合法也不是所有瀏覽器都處理一致。改用 `router.push` 導頁。
 */
export function CategoryChipLink({ categoryId, imageSrc, imageAlt, label }: CategoryChipLinkProps) {
  const router = useRouter();
  return (
    <CategoryChip
      imageSrc={imageSrc}
      imageAlt={imageAlt}
      label={label}
      onClick={() => router.push(`/categories/${categoryId}`)}
    />
  );
}
