/**
 * **每一條路由恰好有一種殼。** 分頁列或頂部列，二擇一。
 *
 * ── 這是這一包最重要的一條測試 ──
 * #30 是「首頁到購物車之間沒有路」，#32 是「商品詳情、購物車、結帳三頁沒有出口」，
 * 而 #32 之所以會發生，正是因為 FE-23 把那三頁的分頁列關掉時**沒有人接手給出口**。
 * 兩次都是「每一頁單獨看都對，串起來走不通」，兩次都活過了所有檢查。
 *
 * 所以這裡不再抄一份「哪幾頁有殼」的清單——那份清單本身就是會漂移的東西。
 * 改成照 `bottomActionBarCollision.test.ts` 的做法**去原始碼真的掃**：
 * 掃出 `apps/storefront/app` 底下每一個 `page.tsx`、推出它的路由，
 * 逐條要求 `shouldShowTabBar` 與 `shouldShowTopBar` 恰好一真一假。
 * 明天有人加一頁、或把某頁的分頁列關掉卻忘了給頂部列，這裡就會紅，
 * 而且紅字裡會印出是哪一條路由。
 *
 * ── 「查了零個對象」也算失敗 ──
 * 掃描器壞掉（走錯根目錄、正規式失手）的結果是「一個對象都沒查到」，
 * 那看起來跟「查過都沒事」一模一樣。所以下面明確斷言掃到的頁面數量下限、
 * 幾條已知路由都在裡面，而且沒有任何頁面推不出路由。
 */
import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { shouldShowTabBar } from '../tabs';
import { shouldShowMobileSiteHeader } from '../mobileNav';
import {
  SHELL_EXCEPTIONS,
  TOP_BAR_RULES,
  isShellException,
  shouldShowTopBar,
} from '../topBar';

/** `app/_lib/__tests__/` 往上兩層就是 `apps/storefront/app`。 */
const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** JSX 用法，不是 import 或註解裡順口提到的名字。 */
const RENDERS_TOP_BAR = /<PageTopBar[\s/>]/;

function allFiles(dir: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name.startsWith('.')) continue;
      found.push(...allFiles(join(dir, entry.name)));
    } else {
      found.push(join(dir, entry.name));
    }
  }
  return found;
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

const ALL_FILES = allFiles(APP_ROOT);

/** 每一個 `page.tsx` ＝ 一條真的存在的路由。 */
const ROUTES = ALL_FILES.filter((file) => file.endsWith(`${sep}page.tsx`))
  .map((file) => ({
    file: relative(APP_ROOT, file).split(sep).join('/'),
    route: routeOf(dirname(file)),
  }))
  .sort((a, b) => a.route.localeCompare(b.route));

/** 哪些頁面**真的**畫了 `<PageTopBar>`（掃原始碼，不是問規則表）。 */
const TOP_BAR_RENDERERS = ALL_FILES.filter(
  (file) =>
    file.endsWith('.tsx') &&
    // 測試自己也會提到這個名字（連這一行都是）。掃的是產品碼，不是測試碼。
    !file.split(sep).includes('__tests__') &&
    RENDERS_TOP_BAR.test(readFileSync(file, 'utf8')),
)
  .map((file) => routeOf(dirname(file)))
  .sort();

describe('掃描器本身是活的', () => {
  it('真的掃到頁面——一個都沒掃到就是掃描器壞了，不是「都沒事」', () => {
    expect(ROUTES.length).toBeGreaterThanOrEqual(16);
  });

  it.each([
    ['/'],
    ['/products'],
    ['/products/sample-id'],
    ['/campaigns'],
    ['/cart'],
    ['/checkout'],
    ['/me'],
    ['/orders'],
    ['/payment/sample-id'],
    ['/payment/result'],
  ])('已知路由 %s 有被掃到', (expectedRoute) => {
    expect(ROUTES.map((entry) => entry.route)).toContain(expectedRoute);
  });

  it('每一條路由都推得出來，沒有空字串或殘留的 (group)／[param]', () => {
    const broken = ROUTES.filter(
      (entry) =>
        entry.route === '' ||
        entry.route.includes('(') ||
        entry.route.includes('[') ||
        entry.route.includes('\\'),
    );
    expect(broken).toEqual([]);
  });
});

describe('每一條路由恰好有一種殼', () => {
  it('分頁列或頂部列，不會兩條都有，也不會一條都沒有', () => {
    const wrong = ROUTES.filter((entry) => !isShellException(entry.route)).flatMap((entry) => {
      const tabBar = shouldShowTabBar(entry.route);
      const topBar = shouldShowTopBar(entry.route);
      if (tabBar !== topBar) return [];
      // 失敗訊息直接講出是哪一頁、現在是幾條殼，看到紅字就知道去哪一份規則表補。
      return [`${entry.route}（${entry.file}）→ ${tabBar ? '兩條殼都有' : '一條殼都沒有'}`];
    });

    expect(wrong).toEqual([]);
  });

  it('例外只有 `/payment/:orderId`，而且它兩條殼都沒有', () => {
    const exceptions = ROUTES.filter((entry) => isShellException(entry.route));
    expect(exceptions.map((entry) => entry.route)).toEqual(['/payment/sample-id']);

    for (const entry of exceptions) {
      expect(shouldShowTabBar(entry.route)).toBe(false);
      expect(shouldShowTopBar(entry.route)).toBe(false);
    }
  });

  it('每一條例外都寫得出理由——沒有理由的例外會被下一個人刪掉，或被下一個人濫用', () => {
    for (const exception of SHELL_EXCEPTIONS) {
      expect(exception.reason.length).toBeGreaterThan(10);
    }
    // 例外清單也不准悄悄長大：新增一條就要來改這裡，順便被迫解釋一次。
    // 順序是規則的一部分（`/payment/result` 必須擋在 `:orderId` 前面），所以連順序一起釘。
    expect(SHELL_EXCEPTIONS.map((exception) => `${exception.route}=${String(exception.exempt)}`)).toEqual([
      '/payment/result=false',
      '/payment/:orderId=true',
    ]);
  });
});

describe('手機全站頁首的獨立規則', () => {
  it('等於分頁列顯示且不是首頁，並且不和頁面頂部列重疊', () => {
    for (const entry of ROUTES) {
      const expected = shouldShowTabBar(entry.route) && entry.route !== '/';
      expect(shouldShowMobileSiteHeader(entry.route), entry.route).toBe(expected);
      expect(
        shouldShowMobileSiteHeader(entry.route) && shouldShowTopBar(entry.route),
        entry.route,
      ).toBe(false);
    }
  });

  it('pathname 拿不到時不畫', () => {
    expect(shouldShowMobileSiteHeader(null)).toBe(false);
  });
});

describe('手機全站頁首掛載位置', () => {
  const layoutFile = join(APP_ROOT, 'layout.tsx');
  const withoutComments = readFileSync(layoutFile, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');

  it('掃描器真的讀到 layout 與三個定位點', () => {
    expect(withoutComments.length).toBeGreaterThan(0);
    expect(withoutComments.indexOf('<SiteHeader')).toBeGreaterThanOrEqual(0);
    expect(withoutComments.indexOf('<MobileSiteHeader')).toBeGreaterThanOrEqual(0);
    expect(withoutComments.indexOf('{children}')).toBeGreaterThanOrEqual(0);
  });

  it('手機頁首在桌面頁首之後、頁面內容之前', () => {
    const siteHeader = withoutComments.indexOf('<SiteHeader');
    const mobileHeader = withoutComments.indexOf('<MobileSiteHeader');
    const children = withoutComments.indexOf('{children}');
    expect(siteHeader).toBeLessThan(mobileHeader);
    expect(mobileHeader).toBeLessThan(children);
  });
});

describe('規則表與原始碼一致：說有頂部列的頁面真的畫了它', () => {
  it('畫了 <PageTopBar> 的頁面 === `shouldShowTopBar` 為真的頁面', () => {
    const declared = ROUTES.filter((entry) => shouldShowTopBar(entry.route))
      .map((entry) => entry.route)
      .sort();

    // 一邊是規則表推出來的，一邊是掃原始碼掃出來的。兩邊都不是抄的。
    expect(TOP_BAR_RENDERERS).toEqual(declared);
  });

  it('掃到的頂部列使用者不是零個', () => {
    expect(TOP_BAR_RENDERERS.length).toBeGreaterThanOrEqual(3);
  });

  it('`TOP_BAR_RULES` 的每一條都對得到一條真的存在的路由', () => {
    const routes = ROUTES.map((entry) => entry.route);
    const orphans = TOP_BAR_RULES.filter(
      (rule) => !routes.some((route) => shouldShowTopBar(route) && matchesLoosely(rule.route, route)),
    ).map((rule) => rule.route);

    expect(orphans).toEqual([]);
  });
});

/** `:param` 對 `sample-id`，逐段。只給上面那一條用。 */
function matchesLoosely(rule: string, route: string): boolean {
  const a = rule.split('/').filter(Boolean);
  const b = route.split('/').filter(Boolean);
  if (a.length !== b.length) return false;
  return a.every((segment, index) => segment.startsWith(':') || segment === b[index]);
}
