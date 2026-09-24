import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { PRIVACY_SECTIONS, PRIVACY_LAST_UPDATED } from '../_content/privacy';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '隱私權政策',
};

/**
 * 閱讀版型照抄服務條款頁（`terms/page.tsx`，FE-45）：容器上限取 `--gg-container-max` 的三分之二、
 * 正文 `--gg-text-base` ＋ `--gg-leading-normal`、同樣的節距與標題層級（派工書 §6.1 第 3 點，
 * 刻意不抽共用元件）。內容全部來自 `_content/privacy.ts`，這裡只負責排版。
 */
export default function PrivacyPage() {
  return (
    <main className="mx-auto flex max-w-[calc(var(--gg-container-max)*2/3)] flex-col gap-[var(--gg-space-8)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
        隱私權政策
      </h1>

      <div className="flex flex-col gap-[var(--gg-space-6)]">
        {PRIVACY_SECTIONS.map((section) => (
          <section key={section.title} className="flex flex-col gap-[var(--gg-space-3)]">
            <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold leading-[var(--gg-leading-tight)] text-fg [overflow-wrap:anywhere]">
              {section.title}
            </h2>
            <Card padding="md" className="flex flex-col gap-[var(--gg-space-4)]">
              {section.paragraphs.map((paragraph) => (
                <p
                  key={paragraph}
                  className="text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg [overflow-wrap:anywhere]"
                >
                  {paragraph}
                </p>
              ))}
            </Card>
          </section>
        ))}
      </div>

      <p className="text-[length:var(--gg-text-xs)] text-fg-muted">最後更新日期：{PRIVACY_LAST_UPDATED}</p>

      <InfoPageFooter currentHref="/privacy" />
    </main>
  );
}
