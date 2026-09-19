import Link from 'next/link';

/**
 * 三個資訊頁的入口清單。首頁頁尾、「我的」頁、以及三個資訊頁彼此的出口
 * 都用同一份清單，不各寫一份——新增第四個資訊頁只要改這裡。
 */
export interface InfoLinkItem {
  readonly href: string;
  readonly label: string;
}

export const INFO_LINKS: readonly InfoLinkItem[] = [
  { href: '/faq', label: '常見問題' },
  { href: '/guide', label: '購買流程' },
  { href: '/about', label: '關於我們' },
  { href: '/terms', label: '服務條款' },
];

export interface InfoLinksProps {
  /** 排除目前所在的那一頁，用於資訊頁彼此的出口（不連回自己）。 */
  readonly exclude?: string;
  readonly className?: string;
}

/** 純呈現：一列資訊連結。首頁頁尾與「我的」頁都用這個元件，不排除任何一條。 */
export function InfoLinks({ exclude, className }: InfoLinksProps = {}) {
  const links = INFO_LINKS.filter((link) => link.href !== exclude);
  return (
    <nav aria-label="常見問題與相關資訊" className={className}>
      <ul className="flex flex-wrap gap-x-[var(--gg-space-4)] gap-y-[var(--gg-space-2)]">
        {links.map((link) => (
          <li key={link.href}>
            <Link
              href={link.href}
              className="text-[length:var(--gg-text-sm)] font-bold text-primary-text underline-offset-4 hover:underline"
            >
              {link.label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
