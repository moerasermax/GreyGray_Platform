import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { ABOUT_PARAGRAPHS } from '../_content/about';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '關於我們',
};

export default function AboutPage() {
  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">關於我們</h1>

      <Card padding="md" className="flex flex-col gap-[var(--gg-space-3)]">
        {ABOUT_PARAGRAPHS.map((paragraph) => (
          <p key={paragraph} className="text-[length:var(--gg-text-sm)] text-fg">
            {paragraph}
          </p>
        ))}
      </Card>

      <InfoPageFooter currentHref="/about" />
    </main>
  );
}
