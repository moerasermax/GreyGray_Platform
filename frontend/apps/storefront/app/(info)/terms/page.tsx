import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { TERMS_SECTIONS, TERMS_LAST_UPDATED } from '../_content/terms';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '服務條款',
};

export default function TermsPage() {
  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">服務條款</h1>

      {TERMS_SECTIONS.map((section) => (
        <section key={section.title} className="flex flex-col gap-[var(--gg-space-3)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{section.title}</h2>
          <Card padding="md" className="flex flex-col gap-[var(--gg-space-2)]">
            {section.paragraphs.map((paragraph) =>
              paragraph.isOwnerNote ? (
                <p
                  key={paragraph.text}
                  className="rounded-md border border-warning/20 bg-warning-subtle p-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] font-bold text-warning-text"
                >
                  ⚠ {paragraph.text}
                </p>
              ) : (
                <p key={paragraph.text} className="text-[length:var(--gg-text-sm)] text-fg">
                  {paragraph.text}
                </p>
              ),
            )}
          </Card>
        </section>
      ))}

      <p className="text-[length:var(--gg-text-xs)] text-fg-muted">最後更新日期：{TERMS_LAST_UPDATED}</p>

      <InfoPageFooter currentHref="/terms" />
    </main>
  );
}
