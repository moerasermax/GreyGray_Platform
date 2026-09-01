/**
 * **兩條固定底部列不可以疊在一起。**
 *
 * ── 為什麼不是把三個路徑再抄一次 ──
 * `tabs.test.ts` 已經釘住「這三條路徑不顯示分頁列」，但那是一份**抄下來的清單**：
 * 明天有人加第四頁用 `BottomActionBar`，那個檔案照樣全綠，
 * 而真瀏覽器上會有兩條固定列疊著蓋住內容——正是派工書 §1 擔心的漂移。
 *
 * 這裡改成**去原始碼裡真的找**：掃 `apps/storefront/app` 底下每一個 `.tsx`，
 * 找出誰用了 `<BottomActionBar`，往上走到最近一層有 `page.tsx` 的目錄推出它的路由，
 * 再要求每一條都被 `shouldShowTabBar` 判成不顯示。
 * 清單不必維護，加頁面就會自己被納入。
 *
 * ── 「查了零個對象」也算失敗 ──
 * 掃描器只要壞掉（走錯根目錄、正規式失手），結果就是「一個對象都沒查到」，
 * 而那看起來跟「查過都沒事」一模一樣。所以下面明確斷言：
 * 至少找到三個、三個已知檔案都在裡面、而且沒有任何一個對不到頁面目錄。
 */
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { shouldShowTabBar } from '../tabs';

/** `app/_lib/__tests__/` 往上兩層就是 `apps/storefront/app`。 */
const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** JSX 用法，不是註解或字串裡順口提到的名字。 */
const USES_BOTTOM_ACTION_BAR = /<BottomActionBar[\s/>]/;

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

/** 這個檔案屬於哪一頁：往上走到最近一層有 `page.tsx` 的目錄。找不到回 `null`。 */
function nearestPageDir(file: string): string | null {
  let dir = dirname(file);
  while (dir.length >= APP_ROOT.length && dir.startsWith(APP_ROOT)) {
    if (existsSync(join(dir, 'page.tsx'))) return dir;
    if (dir === APP_ROOT) return null;
    dir = dirname(dir);
  }
  return null;
}

/** 目錄 → 路由：丟掉 `(group)`，把 `[param]` 換成一個樣本值。 */
function routeOf(pageDir: string): string {
  const segments = relative(APP_ROOT, pageDir)
    .split(sep)
    .filter((segment) => segment.length > 0)
    .filter((segment) => !(segment.startsWith('(') && segment.endsWith(')')))
    .map((segment) =>
      segment.startsWith('[') && segment.endsWith(']') ? 'sample-id' : segment,
    );
  return `/${segments.join('/')}`;
}

const usages = allTsxFiles(APP_ROOT)
  .filter((file) => USES_BOTTOM_ACTION_BAR.test(readFileSync(file, 'utf8')))
  .map((file) => {
    const pageDir = nearestPageDir(file);
    return {
      file: relative(APP_ROOT, file).split(sep).join('/'),
      route: pageDir === null ? null : routeOf(pageDir),
    };
  });

describe('掃描器本身是活的', () => {
  it('真的掃到 BottomActionBar 的使用者——一個都沒掃到就是掃描器壞了，不是「都沒事」', () => {
    expect(usages.length).toBeGreaterThanOrEqual(3);
  });

  it.each([
    ['(shop)/products/[productId]/_components/AddToCartPanel.tsx'],
    ['(checkout)/cart/page.tsx'],
    ['(checkout)/checkout/page.tsx'],
  ])('派工書 §1 列的 %s 有被掃到', (expectedFile) => {
    expect(usages.map((usage) => usage.file)).toContain(expectedFile);
  });

  it('每一個用法都對得到一個頁面目錄——對不到就代表推不出路由，不能靜靜放過', () => {
    expect(usages.filter((usage) => usage.route === null)).toEqual([]);
  });
});

describe('每一個用了 BottomActionBar 的頁面都不畫分頁列', () => {
  it('逐頁檢查（清單是掃出來的，加新頁面會自動被納入）', () => {
    const collisions = usages
      .filter((usage) => usage.route !== null && shouldShowTabBar(usage.route))
      .map((usage) => `${usage.file} → ${usage.route ?? ''}`);

    // 失敗訊息直接把肇事的檔案與路由印出來，看到紅字就知道要去 tabs.ts 加哪一條規則。
    expect(collisions).toEqual([]);
  });
});
