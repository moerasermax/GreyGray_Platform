/**
 * **有動作的提示要留得夠久讓人按得到。**
 *
 * 提示預設 2.5 秒消失——那是「看一眼就好」的長度。裡面放一顆「查看購物車」
 * 之後，2.5 秒是使用者看到它、把手指移過去、按下去的全部時間，
 * 而按不到的按鈕比沒有按鈕更糟：它讓人以為自己按錯了。
 *
 * 這件事沒辦法在 `Toast` 元件裡自己保證（`duration` 是呼叫端傳的，
 * 而呼叫端才知道自己的情境），所以改成去原始碼掃每一個 `<Toast>`：
 * 有 `action` 的，`duration` 一律 ≥ 5000。
 *
 * 掃到零個算失敗——掃描器壞掉跟「都沒事」長得一模一樣。
 */
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** 有動作的提示至少要留這麼久。 */
const MIN_DURATION_WITH_ACTION = 5000;

function allTsxFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name.startsWith('.')) continue;
      found.push(...allTsxFiles(join(dir, entry.name)));
    } else if (entry.name.endsWith('.tsx')) {
      found.push(join(dir, entry.name));
    }
  }
  return found;
}

interface ToastUsage {
  readonly file: string;
  readonly source: string;
  readonly hasAction: boolean;
  readonly duration: number | null;
}

/** 從 `<Toast ... />` 的整段文字裡切出每一個用法。屬性可以跨行，所以用 `[^]` 吃換行。 */
function toastUsagesIn(file: string): ToastUsage[] {
  const text = readFileSync(file, 'utf8');
  const matches = text.match(/<Toast[\s][^]*?\/>/g) ?? [];
  return matches.map((source) => {
    const duration = /duration=\{(\d+)\}/.exec(source);
    return {
      file: relative(APP_ROOT, file).split(sep).join('/'),
      source,
      hasAction: /\baction=\{/.test(source),
      duration: duration?.[1] === undefined ? null : Number(duration[1]),
    };
  });
}

const USAGES = allTsxFiles(APP_ROOT)
  .filter((file) => !file.split(sep).includes('__tests__'))
  .flatMap(toastUsagesIn);

const WITH_ACTION = USAGES.filter((usage) => usage.hasAction);

describe('掃描器本身是活的', () => {
  it('真的掃到 <Toast> 的用法', () => {
    expect(USAGES.length).toBeGreaterThanOrEqual(4);
  });

  it('真的掃到帶 action 的用法——零個代表掃描器壞了，或這一包根本沒做', () => {
    expect(WITH_ACTION.length).toBeGreaterThanOrEqual(2);
  });

  it('每一個用法都解析得出 duration（沒寫的另外處理，不是靜靜跳過）', () => {
    const noDuration = USAGES.filter((usage) => usage.duration === null).map((usage) => usage.file);
    // 前台目前每一個提示都自動消失。哪天有人刻意不設 duration，這裡會紅、逼他來說明。
    expect(noDuration).toEqual([]);
  });
});

describe('有動作的提示要留得夠久', () => {
  it(`帶 action 的 <Toast> duration 一律 ≥ ${MIN_DURATION_WITH_ACTION}`, () => {
    const tooShort = WITH_ACTION.filter(
      (usage) => usage.duration === null || usage.duration < MIN_DURATION_WITH_ACTION,
    ).map((usage) => `${usage.file} → duration=${String(usage.duration)}`);

    expect(tooShort).toEqual([]);
  });

  it('★ 加入購物車的成功提示真的帶了「查看購物車」→ /cart（#32 的修法本體）', () => {
    const addToCart = WITH_ACTION.filter((usage) => usage.source.includes('已加入購物車'));
    expect(addToCart.map((usage) => usage.file).sort()).toEqual([
      '(shop)/campaigns/[campaignId]/_components/CampaignOfferRow.tsx',
      '(shop)/products/[productId]/_components/AddToCartPanel.tsx',
    ]);

    for (const usage of addToCart) {
      expect(usage.source).toContain("label: '查看購物車'");
      expect(usage.source).toContain("href: '/cart'");
    }
  });
});
