import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { FAQ_GROUPS } from '../_content/faq';
import { InfoPageFooter } from '../_components/InfoPageFooter';

/**
 * 只寫短標題——根 layout 的 `template: '%s｜GreyGray'` 會補上後綴，
 * 自己寫完整字串會變成「常見問題｜GreyGray｜GreyGray」。
 */
export const metadata: Metadata = {
  title: '常見問題',
};

/** 用原生 `<details>`／`<summary>`：不要 JS，鍵盤天生可用。 */
export default function FaqPage() {
  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">常見問題</h1>

      {FAQ_GROUPS.map((group) => (
        <section key={group.title} className="flex flex-col gap-[var(--gg-space-3)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{group.title}</h2>
          <div className="flex flex-col gap-[var(--gg-space-2)]">
            {group.items.map((item) => (
              <Card key={item.question} padding="md">
                <details>
                  <summary className="cursor-pointer font-bold text-fg">{item.question}</summary>
                  <p className="mt-[var(--gg-space-2)] text-[length:var(--gg-text-sm)] text-fg-muted">
                    {item.answer}
                  </p>
                </details>
              </Card>
            ))}
          </div>
        </section>
      ))}

      <InfoPageFooter currentHref="/faq" />
    </main>
  );
}
