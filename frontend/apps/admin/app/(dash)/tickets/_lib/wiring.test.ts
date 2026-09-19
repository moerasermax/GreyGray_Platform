/**
 * 掃原始碼確認幾件不方便用 `renderToStaticMarkup` 測、但機械掃得到的事
 * （這個 workspace 沒有 jsdom，`page.tsx` 本身是有 `useEffect` 抓資料的 client component，
 * 渲染結果測不到——照 `orders/page.tsx` 的既有慣例，這種頁面本來就不直接測渲染）。
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const TICKETS_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
const PAGE_SRC = readFileSync(join(TICKETS_DIR, 'page.tsx'), 'utf8');
const LAYOUT_SRC = readFileSync(join(TICKETS_DIR, '..', 'layout.tsx'), 'utf8');

describe('側邊選單有「客服訊息」入口', () => {
  it('NAV_DEFINITIONS 有 /tickets', () => {
    expect(LAYOUT_SRC).toContain("href: '/tickets'");
    expect(LAYOUT_SRC).toContain('客服訊息');
  });
});

describe('列表是游標分頁 ＋「載入更多」，不是數字分頁（派工書 §0.2 明講後台沒有數字分頁元件）', () => {
  it('用 nextCursor／cursor，不是 page/pageNumber', () => {
    expect(PAGE_SRC).toContain('nextCursor');
    expect(PAGE_SRC).toContain('cursor');
    expect(PAGE_SRC).not.toMatch(/pageNumber|pageIndex/);
  });

  it('有「載入更多」按鈕文字', () => {
    expect(PAGE_SRC).toContain('載入更多');
  });
});

describe('至少要能只看未結案的（§1.2）', () => {
  it('狀態篩選預設 open', () => {
    expect(PAGE_SRC).toMatch(/useState<SupportTicketStatus \| ''>\('open'\)/);
  });
});

describe('409 已被別人處理：要刷新，不要卡住（§1.2）', () => {
  it('page.tsx 把 onConflict 接到重讀單一工單，而不是留白或整頁重整', () => {
    expect(PAGE_SRC).toContain('onConflict');
    expect(PAGE_SRC).toContain('refreshOne');
    expect(PAGE_SRC).toContain('getSupportTicket');
  });
});

describe('resolve 的角色限制沒有被前端拿掉', () => {
  it('page.tsx 讀 session 角色算出 canResolve，傳給 Dialog', () => {
    expect(PAGE_SRC).toContain('hasRequiredRole');
    expect(PAGE_SRC).toContain("'Operator'");
    expect(PAGE_SRC).toContain('canResolve');
  });
});
