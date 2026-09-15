/**
 * 資訊頁（常見問題／購買流程／關於我們）的入口、出口與文案紅線。
 *
 * 照 `_lib/__tests__/pageShell.test.ts` 的精神：**去原始碼掃，不抄清單；
 * 掃到零個算失敗**。這個專案已經在同一個位置踩過三次「每一頁單獨看都對，
 * 串起來走不通」（#30 前台無導覽、#32 三頁無出口、`/wallet` 零入口），
 * 所以入口／出口不是「看起來有連結就好」，是明確斷言連得到哪裡。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { shouldShowTabBar } from '../../_lib/tabs';
import { shouldShowTopBar } from '../../_lib/topBar';
import { ABOUT_PARAGRAPHS } from '../_content/about';
import { FAQ_GROUPS } from '../_content/faq';
import { GUIDE_SECTIONS } from '../_content/guide';
import { INFO_LINKS } from '../_components/InfoLinks';

/** `(info)/__tests__/` 往上兩層就是 `apps/storefront/app`。 */
const APP_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

function readApp(relativePath: string): string {
  return readFileSync(join(APP_ROOT, relativePath), 'utf8');
}

const INFO_PAGES = [
  { route: '/faq', file: '(info)/faq/page.tsx', title: '常見問題' },
  { route: '/guide', file: '(info)/guide/page.tsx', title: '購買流程' },
  { route: '/about', file: '(info)/about/page.tsx', title: '關於我們' },
] as const;

describe('三個資訊頁都存在，恰好有一種殼', () => {
  it.each(INFO_PAGES)('$route：分頁列顯示、頂部列不顯示', ({ route }) => {
    expect(shouldShowTabBar(route)).toBe(true);
    expect(shouldShowTopBar(route)).toBe(false);
  });

  it.each(INFO_PAGES)('$file 存在且讀得到內容', ({ file }) => {
    expect(readApp(file).length).toBeGreaterThan(0);
  });
});

describe('metadata.title 只寫短標題', () => {
  it.each(INFO_PAGES)('$file 的 title 是 "$title"，不含 GreyGray（根 layout 的 template 會補）', ({ file, title }) => {
    const source = readApp(file);
    const match = source.match(/title:\s*'([^']*)'/);
    expect(match).not.toBeNull();
    expect(match?.[1]).toBe(title);
    expect(match?.[1]).not.toContain('GreyGray');
  });
});

describe('入口：匿名訪客與登入使用者都連得到三個資訊頁', () => {
  it('首頁（(shop)/page.tsx）渲染 InfoLinks——匿名訪客的唯一入口', () => {
    expect(readApp('(shop)/page.tsx')).toMatch(/<InfoLinks\b/);
  });

  it('「我的」頁（(account)/me/page.tsx）渲染 InfoLinks，並多一筆 /favorites 連結', () => {
    const source = readApp('(account)/me/page.tsx');
    expect(source).toMatch(/<InfoLinks\b/);
    expect(source).toContain("href: '/favorites'");
  });

  it('InfoLinks 本身就是三個資訊頁的清單——上面兩個入口最終落地在這裡', () => {
    expect(INFO_LINKS.map((link) => link.href).sort()).toEqual(['/about', '/faq', '/guide']);
  });
});

describe('出口：每個資訊頁都連得到另外兩頁與首頁', () => {
  it.each(INFO_PAGES)('$file 渲染 InfoPageFooter，並帶自己的 currentHref', ({ file, route }) => {
    const source = readApp(file);
    expect(source).toMatch(/<InfoPageFooter\b/);
    expect(source).toContain(`currentHref="${route}"`);
  });

  it('InfoPageFooter 排除目前頁面、其餘資訊頁都在，並且一定連回首頁', () => {
    const source = readApp('(info)/_components/InfoPageFooter.tsx');
    expect(source).toMatch(/exclude=\{currentHref\}/);
    expect(source).toMatch(/href="\/"/);
  });

  it('三個資訊頁互相排除自己之後，兩兩都連得到對方（掃 INFO_LINKS 本身沒有遺漏任何一頁）', () => {
    for (const page of INFO_PAGES) {
      const others = INFO_LINKS.filter((link) => link.href !== page.route).map((link) => link.href);
      expect(others.sort()).toEqual(
        INFO_PAGES.map((other) => other.route)
          .filter((route) => route !== page.route)
          .sort(),
      );
    }
  });
});

describe('文案資料的形狀', () => {
  it('常見問題恰好四組', () => {
    expect(FAQ_GROUPS.length).toBe(4);
  });

  it('常見問題題數在 10～14 之間，沒有空字串', () => {
    const totalQuestions = FAQ_GROUPS.reduce((sum, group) => sum + group.items.length, 0);
    expect(totalQuestions).toBeGreaterThanOrEqual(10);
    expect(totalQuestions).toBeLessThanOrEqual(14);

    for (const group of FAQ_GROUPS) {
      expect(group.title.trim().length).toBeGreaterThan(0);
      for (const item of group.items) {
        expect(item.question.trim().length).toBeGreaterThan(0);
        expect(item.answer.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('購買流程恰好兩段，每段 4～6 步，沒有空字串', () => {
    expect(GUIDE_SECTIONS.length).toBe(2);
    for (const section of GUIDE_SECTIONS) {
      expect(section.steps.length).toBeGreaterThanOrEqual(4);
      expect(section.steps.length).toBeLessThanOrEqual(6);
      for (const step of section.steps) {
        expect(step.trim().length).toBeGreaterThan(0);
      }
    }
  });

  it('關於我們有內容，沒有空字串', () => {
    expect(ABOUT_PARAGRAPHS.length).toBeGreaterThan(0);
    for (const paragraph of ABOUT_PARAGRAPHS) {
      expect(paragraph.trim().length).toBeGreaterThan(0);
    }
  });
});

/**
 * ── 不准出現沒確認過的承諾 ──
 *
 * 這是「不存在」型斷言：掃過所有文案資料，逐類確認沒有出現
 * 聯絡方式、鑑賞期、或任何時限承諾。唯一允許的數字是三個運費金額。
 *
 * 這裡故意把「哪些字串會被掃」抽成一個陣列，而不是分別在每個 it 裡各自
 * 讀一次 `FAQ_GROUPS`／`GUIDE_SECTIONS`／`ABOUT_PARAGRAPHS`——
 * 這樣自驗時只要在這個陣列裡臨時塞一個反例，就能同時驗到所有規則。
 */
const ALL_COPY_TEXT: readonly string[] = [
  ...FAQ_GROUPS.flatMap((group) => [group.title, ...group.items.flatMap((item) => [item.question, item.answer])]),
  ...GUIDE_SECTIONS.flatMap((section) => [section.title, ...section.steps]),
  ...ABOUT_PARAGRAPHS,
];

const CONTACT_INFO_RE = /電話|LINE|Line\s*ID|Email|信箱|地址|營業時間/i;
const APPRAISAL_PERIOD_RE = /鑑賞期/;
const IMMEDIATE_PROMISE_RE = /隔日|當天|立即出貨|盡快/;

/** 阿拉伯數字或中文數字 ＋ 常見時間單位；中間允許一個「個」字（例如「三個工作天」）。 */
const TIME_LIMIT_RE = /(?:[0-9]+|[一二三四五六七八九十百千萬]+)\s*(?:個)?\s*(?:工作天|營業日|天|日|小時|hr|週)/i;

describe('不准出現沒確認過的承諾（客服電話、地址、LINE、Email、營業時間、鑑賞期、任何時限）', () => {
  it('文案資料裡沒有聯絡方式', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => CONTACT_INFO_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('文案資料裡沒有「鑑賞期」（ADR-025：法律適用由老闆判斷）', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => APPRAISAL_PERIOD_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('文案資料裡沒有「隔日」「當天」「立即出貨」「盡快」這類出貨時間承諾', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => IMMEDIATE_PROMISE_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('文案資料裡沒有任何數字＋時間單位的時限承諾（唯一允許的數字是三個運費金額）', () => {
    const offenders = ALL_COPY_TEXT.filter((text) => TIME_LIMIT_RE.test(text));
    expect(offenders).toEqual([]);
  });

  it('三個運費金額確實還在（不是規則寫過頭把合法內容一起濾掉）', () => {
    const joined = ALL_COPY_TEXT.join(' ');
    expect(joined).toContain('NT$60');
    expect(joined).toContain('NT$120');
    expect(joined).toContain('NT$0');
  });
});
