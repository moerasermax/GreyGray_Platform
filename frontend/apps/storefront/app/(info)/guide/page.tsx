import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { GUIDE_SECTIONS } from '../_content/guide';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '購買流程',
};

export default function GuidePage() {
  return (
    <main className="mx-auto flex max-w-[var(--gg-container-max)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold text-fg">購買流程</h1>

      {GUIDE_SECTIONS.map((section) => (
        <section key={section.title} className="flex flex-col gap-[var(--gg-space-3)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold text-fg">{section.title}</h2>
          <Card padding="md">
            <ol className="flex flex-col gap-[var(--gg-space-3)] list-decimal pl-[var(--gg-space-5)]">
              {section.steps.map((step) => (
                <li key={step} className="text-[length:var(--gg-text-sm)] text-fg">
                  {step}
                </li>
              ))}
            </ol>
          </Card>
        </section>
      ))}

      <InfoPageFooter currentHref="/guide" />
    </main>
  );
}
