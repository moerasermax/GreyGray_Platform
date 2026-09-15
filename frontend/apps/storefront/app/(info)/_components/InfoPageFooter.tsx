import Link from 'next/link';
import { InfoLinks } from './InfoLinks';

/**
 * 資訊頁底部的出口：連到另外兩個資訊頁 ＋ 回首頁。
 * 分頁列本來就在（新頁面預設有殼，`_lib/tabs.ts` 是黑名單），
 * 但三頁彼此之間、以及回首頁的路，要靠這個元件明確給——
 * 不能只靠分頁列的「我的」／「首頁」間接繞回來。
 */
export interface InfoPageFooterProps {
  /** 目前所在的資訊頁路由，用來從清單中排除自己。 */
  readonly currentHref: string;
}

export function InfoPageFooter({ currentHref }: InfoPageFooterProps) {
  return (
    <footer className="flex flex-col gap-[var(--gg-space-3)] border-t border-border-soft pt-[var(--gg-space-4)]">
      <InfoLinks exclude={currentHref} />
      <Link
        href="/"
        className="text-[length:var(--gg-text-sm)] text-fg-muted underline-offset-4 hover:underline"
      >
        回首頁
      </Link>
    </footer>
  );
}
