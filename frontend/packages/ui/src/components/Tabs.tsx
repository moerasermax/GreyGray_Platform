'use client';

import { useRef } from 'react';
import { cn } from './internal/cn';

export interface TabItem {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface TabsProps {
  items: TabItem[];
  value: string;
  onChange: (value: string) => void;
  className?: string;
}

/** 左右鍵可切換（roving tabindex）。 */
export function Tabs({ items, value, onChange, className }: TabsProps) {
  const buttonRefs = useRef<Array<HTMLButtonElement | null>>([]);

  function focusTab(index: number) {
    const enabled = items.map((item, i) => ({ item, i })).filter(({ item }) => !item.disabled);
    if (enabled.length === 0) return;
    const currentPos = enabled.findIndex(({ i }) => i === index);
    const nextPos = (currentPos + enabled.length) % enabled.length;
    const target = enabled[nextPos]!;
    buttonRefs.current[target.i]?.focus();
    onChange(target.item.value);
  }

  function handleKeyDown(event: React.KeyboardEvent, index: number) {
    if (event.key === 'ArrowRight') {
      event.preventDefault();
      focusTab(index + 1);
    } else if (event.key === 'ArrowLeft') {
      event.preventDefault();
      focusTab(index - 1);
    } else if (event.key === 'Home') {
      event.preventDefault();
      focusTab(0);
    } else if (event.key === 'End') {
      event.preventDefault();
      focusTab(items.length - 1);
    }
  }

  return (
    <div
      role="tablist"
      className={cn('flex gap-[var(--gg-space-2)] border-b border-border-soft', className)}
    >
      {items.map((item, index) => {
        const selected = item.value === value;
        return (
          <button
            key={item.value}
            ref={(el) => {
              buttonRefs.current[index] = el;
            }}
            type="button"
            role="tab"
            aria-selected={selected}
            disabled={item.disabled}
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(item.value)}
            onKeyDown={(event) => handleKeyDown(event, index)}
            className={cn(
              'border-b-2 px-[var(--gg-space-3)] py-[var(--gg-space-2)]',
              'text-[length:var(--gg-text-sm)] font-bold',
              'transition-colors duration-[var(--gg-duration-fast)] ease-out-soft',
              'disabled:opacity-50',
              selected
                ? 'border-primary text-primary-text'
                : 'border-transparent text-fg-muted hover:text-fg',
            )}
          >
            {item.label}
          </button>
        );
      })}
    </div>
  );
}
