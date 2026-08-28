'use client';

import { useState } from 'react';
import { IconChevronDown } from '@greygray/ui';
import { cn } from '../_lib/cn';

export interface ExplainDisclosureProps {
  title?: string;
  items: readonly string[];
  className?: string;
}

/**
 * 計價說明，可摺疊。**客服要能直接回答「為什麼收這麼多」**（docs/06 FE-4），
 * 所以預設就顯示標題，內容展開後是完整的 `explain` 陣列，不做任何摘要或改寫。
 */
export function ExplainDisclosure({ title = '這筆金額怎麼算的？', items, className }: ExplainDisclosureProps) {
  const [open, setOpen] = useState(false);
  if (items.length === 0) return null;

  return (
    <div className={cn('rounded-[var(--gg-radius-sm)] border border-border-soft bg-surface-sunken', className)}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="flex w-full items-center justify-between gap-[var(--gg-space-2)] px-[var(--gg-space-4)] py-[var(--gg-space-3)] text-left text-[length:var(--gg-text-sm)] font-bold text-fg"
      >
        {title}
        <IconChevronDown
          className={cn('transition-transform duration-[var(--gg-duration-fast)]', open && 'rotate-180')}
        />
      </button>
      {open && (
        <ul className="flex flex-col gap-[var(--gg-space-1)] px-[var(--gg-space-4)] pb-[var(--gg-space-3)] text-[length:var(--gg-text-sm)] text-fg-muted">
          {items.map((item, index) => (
            <li key={index}>・{item}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
