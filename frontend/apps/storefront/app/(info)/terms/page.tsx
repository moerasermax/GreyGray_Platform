import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { TERMS_SECTIONS, TERMS_LAST_UPDATED } from '../_content/terms';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '服務條款',
};

/**
 * 閱讀版型與 FE-43 的關於／FAQ 一致：容器上限取 `--gg-container-max` 的三分之二、
 * 正文 `--gg-text-base` ＋ `--gg-leading-normal`，避免一行塞超過 70 個中文字。
 * 內容全部來自 `_content/terms.ts`，這裡只負責排版。
 */
export default function TermsPage() {
  return (
    <main className="mx-auto flex max-w-[calc(var(--gg-container-max)*2/3)] flex-col gap-[var(--gg-space-8)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
        服務條款
      </h1>

      <div className="flex flex-col gap-[var(--gg-space-6)]">
        {TERMS_SECTIONS.map((section) => (
          <section key={section.title} className="flex flex-col gap-[var(--gg-space-3)]">
            <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold leading-[var(--gg-leading-tight)] text-fg [overflow-wrap:anywhere]">
              {section.title}
            </h2>
            <Card padding="md" className="flex flex-col gap-[var(--gg-space-4)]">
              {section.paragraphs.map((paragraph) =>
                paragraph.isOwnerNote ? (
                  <p
                    key={paragraph.text}
                    className="rounded-md border border-warning/20 bg-warning-subtle p-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] font-bold leading-[var(--gg-leading-normal)] text-warning-text [overflow-wrap:anywhere]"
                  >
                    ⚠ {paragraph.text}
                  </p>
                ) : (
                  <p
                    key={paragraph.text}
                    className="text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg [overflow-wrap:anywhere]"
                  >
                    {paragraph.text}
                  </p>
                ),
              )}
            </Card>
          </section>
        ))}
      </div>

      <p className="text-[length:var(--gg-text-xs)] text-fg-muted">最後更新日期：{TERMS_LAST_UPDATED}</p>

      <InfoPageFooter currentHref="/terms" />
    </main>
  );
}
