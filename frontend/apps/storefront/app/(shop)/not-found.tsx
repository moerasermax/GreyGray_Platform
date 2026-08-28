import Link from 'next/link';
import { Button, EmptyState } from '@greygray/ui';

export default function ShopNotFound() {
  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col px-[var(--gg-space-4)] py-[var(--gg-space-8)]">
      <EmptyState
        title="找不到這個頁面"
        description="商品或開團可能已經下架，回首頁看看其他選擇。"
        action={
          <Link href="/">
            <Button size="sm">回首頁</Button>
          </Link>
        }
      />
    </main>
  );
}
