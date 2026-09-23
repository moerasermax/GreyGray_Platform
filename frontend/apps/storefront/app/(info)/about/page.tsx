import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { ABOUT_PARAGRAPHS } from '../_content/about';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '關於我們',
};

export default function AboutPage() {
  return (
    <main className="mx-auto flex max-w-[calc(var(--gg-container-max)*2/3)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
        關於我們
      </h1>

      <Card padding="md" className="flex flex-col gap-[var(--gg-space-4)]">
        {ABOUT_PARAGRAPHS.map((paragraph) => (
          <p
            key={paragraph}
            className="text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg [overflow-wrap:anywhere]"
          >
            {paragraph}
          </p>
        ))}
      </Card>

      <InfoPageFooter currentHref="/about" />
    </main>
  );
}
