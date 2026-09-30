import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import * as React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import {
  PasswordInput,
  passwordInputState,
  passwordMaskInput,
  passwordToggleInput,
} from '@greygray/ui/admin';

(globalThis as unknown as { React: typeof React }).React = React;

const FRONTEND_ROOT = join(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  '..',
  '..',
  '..',
);
const PASSWORD_INPUT = join(
  FRONTEND_ROOT,
  'packages',
  'ui',
  'src',
  'admin',
  'PasswordInput.tsx',
);
const SOURCE = readFileSync(PASSWORD_INPUT, 'utf8');
const CODE = SOURCE.replace(/\/\*[^]*?\*\//g, '').replace(/\/\/.*$/gm, '');

describe('後台 PasswordInput 靜態標記', () => {
  it('T1：預設為 password，保留 current-password 與 id', () => {
    const html = renderToStaticMarkup(
      <PasswordInput id="admin-password" autoComplete="current-password" />,
    );
    expect(html).toContain('type="password"');
    expect(html).toMatch(/autocomplete="current-password"/i);
    expect(html).toContain('id="admin-password"');
  });

  it('T2：切換鈕有正確語意，不使用 aria-pressed', () => {
    const html = renderToStaticMarkup(
      <PasswordInput id="admin-password" autoComplete="current-password" />,
    );
    expect(html).toMatch(/<button[^>]*type="button"/);
    expect(html).toContain('aria-label="顯示密碼"');
    expect(html).toContain('aria-controls="admin-password"');
    expect(html).not.toContain('aria-pressed');
  });

  it('T3：保留 new-password', () => {
    const html = renderToStaticMarkup(
      <PasswordInput id="new-password" autoComplete="new-password" />,
    );
    expect(html).toMatch(/autocomplete="new-password"/i);
  });

  it('T8：invalid 轉傳成 aria-invalid', () => {
    const html = renderToStaticMarkup(
      <PasswordInput id="invalid-password" autoComplete="current-password" invalid />,
    );
    expect(html).toContain('aria-invalid="true"');
  });
});

describe('後台 PasswordInput 純函式', () => {
  it('T4：隱藏與顯示狀態對應型別和標籤', () => {
    expect(passwordInputState(false)).toEqual({
      inputType: 'password',
      toggleLabel: '顯示密碼',
    });
    expect(passwordInputState(true)).toEqual({
      inputType: 'text',
      toggleLabel: '隱藏密碼',
    });
  });

  it('T5：先改 type，再還原選取，最後聚焦', () => {
    const calls: string[] = [];
    let type = 'password';
    const input = {
      get type() {
        return type;
      },
      set type(value: string) {
        type = value;
        calls.push(`type:${value}`);
      },
      selectionStart: 2,
      selectionEnd: 5,
      setSelectionRange(start: number, end: number) {
        calls.push(`selection:${start}-${end}`);
      },
      focus() {
        calls.push('focus');
      },
    };

    expect(passwordToggleInput(input)).toBe(true);
    expect(type).toBe('text');
    expect(calls).toEqual(['type:text', 'selection:2-5', 'focus']);
  });

  it('T5：選取位置為 null 時不還原選取，但仍聚焦', () => {
    const calls: string[] = [];
    const input = {
      type: 'text',
      selectionStart: null,
      selectionEnd: null,
      setSelectionRange() {
        calls.push('selection');
      },
      focus() {
        calls.push('focus');
      },
    };

    expect(passwordToggleInput(input)).toBe(false);
    expect(input.type).toBe('password');
    expect(calls).toEqual(['focus']);
  });

  it('T6：送出處理會切回 password', () => {
    const input = { type: 'text' };
    passwordMaskInput(input);
    expect(input.type).toBe('password');
  });
});

describe('後台 PasswordInput 原始碼守衛', () => {
  it('T7：掃描器真的讀到 PasswordInput', () => {
    expect(SOURCE).toContain('export function PasswordInput');
  });

  it('T7：去掉註解後只畫一個 input', () => {
    expect(CODE.match(/<(input|Input)[\s/>]/g)).toHaveLength(1);
  });
});
