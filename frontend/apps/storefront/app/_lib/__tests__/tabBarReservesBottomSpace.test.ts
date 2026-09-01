/**
 * **分頁列的高度必須恰好等於 `globals.css` 給 `<body>` 的底部留白。**
 *
 * ── 這一條在守什麼 ──
 * 分頁列是 `fixed bottom-0`，會蓋住最後一段內容。全站的解法只有一處：
 * `globals.css` 給 `<body>` 補的
 * `padding-bottom: calc(var(--gg-bottom-bar-height) + env(safe-area-inset-bottom, 0px))`，
 * **每一頁都有**（不是只有商品詳情那幾頁）。
 * 分頁列的高度只要寫成同一個算式，內容就恰好不被蓋住，也不會多出一整條的空白。
 *
 * 兩邊各寫各的就會漂移，而漂移不會有編譯錯誤：
 *   · 分頁列變高 → 最後一行內容被蓋住（#30 那種只有真瀏覽器才看得到的病）
 *   · 分頁列變矮 → 底下留一條空白
 *   · 有人改掉 `globals.css` 那一行 → 兩種都可能，而且沒有任何東西會出聲
 * 所以這裡**去讀 `globals.css` 的原始碼**逐字比對，不是比對一個抄下來的字串。
 *
 * ★ 順帶說明 `layout.tsx` 為什麼沒有再包一層 `pb-*`：
 * 那個留白已經存在了。再加一次會變成 144px，多出整整一條列的空白。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { TAB_BAR_HEIGHT, TAB_BAR_SAFE_AREA_PADDING } from '../tabs';

const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const GLOBALS_CSS = readFileSync(join(APP_ROOT, 'globals.css'), 'utf8');

/** 連續空白一律收成一個空格，才不會被排版差異誤判。 */
function normalizeSpaces(value: string): string {
  return value.replace(/\s+/g, ' ').trim();
}

/** `globals.css` 裡 `body { ... }` 區塊的 `padding-bottom` 值。 */
function bodyPaddingBottom(): string | null {
  const block = /\bbody\s*\{([^}]*)\}/.exec(GLOBALS_CSS);
  if (!block?.[1]) return null;
  const declaration = /padding-bottom\s*:\s*([^;]+);/.exec(block[1]);
  return declaration?.[1] ?? null;
}

describe('globals.css 的 body 留白', () => {
  it('找得到（找不到就是這個測試瞎了，不是「沒問題」）', () => {
    expect(bodyPaddingBottom()).not.toBeNull();
  });

  it('是全站每一頁都有的——不是包在某個 media query 或某一頁的 class 裡', () => {
    expect(GLOBALS_CSS).toContain('padding-bottom: calc(var(--gg-bottom-bar-height)');
  });
});

describe('分頁列高度 === body 留白', () => {
  it('逐字相同，所以內容既不會被蓋住、也不會多一條空白', () => {
    expect(normalizeSpaces(TAB_BAR_HEIGHT)).toBe(normalizeSpaces(bodyPaddingBottom() ?? ''));
  });

  it('高度是用 token 算的，不是寫死的 px（前端四條：尺寸只從 token 取）', () => {
    expect(TAB_BAR_HEIGHT).toContain('var(--gg-bottom-bar-height)');
    // `env(...)` 裡的 `0px` 是 env 變數的後備值，不是寫死的尺寸，比對前先拿掉整個 env() 呼叫。
    const withoutEnvFallback = TAB_BAR_HEIGHT.replace(/env\([^)]*\)/g, '');
    expect(withoutEnvFallback).not.toMatch(/\d+px/);
  });

  it('含 safe-area——iPhone 的 home indicator 會吃掉一截', () => {
    expect(TAB_BAR_HEIGHT).toContain('env(safe-area-inset-bottom, 0px)');
    expect(TAB_BAR_SAFE_AREA_PADDING).toBe('env(safe-area-inset-bottom, 0px)');
  });
});
