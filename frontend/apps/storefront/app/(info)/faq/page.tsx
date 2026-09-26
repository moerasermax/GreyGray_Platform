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
    <main className="mx-auto flex max-w-[calc(var(--gg-container-max)*2/3)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <div className="flex flex-col gap-[var(--gg-space-2)]">
        <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
          常見問題
        </h1>
        <p className="text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg-muted">
          找不到答案？點畫面右下角的客服小幫手留言，我們會儘快回覆。
        </p>
      </div>

      {FAQ_GROUPS.map((group) => (
        <section key={group.title} className="flex flex-col gap-[var(--gg-space-3)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold leading-[var(--gg-leading-tight)] text-fg">
            {group.title}
          </h2>
          <div className="flex flex-col gap-[var(--gg-space-3)]">
            {group.items.map((item) => (
              <Card key={item.question} padding="md">
                <details>
                  <summary className="cursor-pointer rounded-card text-[length:var(--gg-text-base)] font-bold leading-[var(--gg-leading-normal)] text-fg [overflow-wrap:anywhere] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary-strong">
                    {item.question}
                  </summary>
                  <p className="mt-[var(--gg-space-3)] text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg-muted [overflow-wrap:anywhere]">
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
