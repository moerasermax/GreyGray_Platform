import type { Metadata } from 'next';
import { Card } from '@greygray/ui';
import { GUIDE_SECTIONS } from '../_content/guide';
import { InfoPageFooter } from '../_components/InfoPageFooter';

export const metadata: Metadata = {
  title: '購買流程',
};

export default function GuidePage() {
  return (
    <main className="mx-auto flex max-w-[calc(var(--gg-container-max)*2/3)] flex-col gap-[var(--gg-space-6)] px-[var(--gg-space-4)] py-[var(--gg-space-6)]">
      <h1 className="font-display text-[length:var(--gg-text-3xl)] font-extrabold leading-[var(--gg-leading-tight)] text-fg">
        購買流程
      </h1>

      {GUIDE_SECTIONS.map((section) => (
        <section key={section.title} className="flex flex-col gap-[var(--gg-space-3)]">
          <h2 className="font-display text-[length:var(--gg-text-lg)] font-bold leading-[var(--gg-leading-tight)] text-fg">
            {section.title}
          </h2>
          <Card padding="md">
            <ol className="flex flex-col gap-[var(--gg-space-4)] list-decimal pl-[var(--gg-space-6)]">
              {section.steps.map((step) => (
                <li
                  key={step}
                  className="pl-[var(--gg-space-1)] text-[length:var(--gg-text-base)] leading-[var(--gg-leading-normal)] text-fg [overflow-wrap:anywhere]"
                >
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
