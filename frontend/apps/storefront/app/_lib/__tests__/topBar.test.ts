/**
 * 頂部列的判斷邏輯。
 *
 * ── 這裡最要緊的一段是「返回的後備目標」 ──
 * 使用者可能是直接打網址、從 LINE 分享點進來、掃 QR code 進來的，
 * 那時候**沒有站內上一頁**。返回鍵在那種情況下按了沒反應，
 * 跟 #32 的「沒有出口」是同一件事，只是多了一顆看起來會動的按鈕。
 */
import { describe, expect, it } from 'vitest';
import {
  TOP_BAR_RULES,
  backHrefFor,
  isShellException,
  normalizePathname,
  shouldShowCartLink,
  shouldShowMenuButton,
  shouldShowTopBar,
  shouldUseHistoryBack,
  topBarRuleFor,
} from '../topBar';

describe('shouldShowTopBar：只有分頁列讓位的那三頁有頂部列', () => {
  it.each([['/products/abc123'], ['/cart'], ['/checkout']])('%s 有頂部列', (path) => {
    expect(shouldShowTopBar(path)).toBe(true);
  });

  it.each([
    ['/'],
    ['/products'],
    ['/campaigns'],
    ['/campaigns/abc123'],
    ['/categories/abc123'],
    ['/orders'],
    ['/orders/abc123'],
    ['/wallet'],
    ['/addresses'],
    ['/login'],
    ['/register'],
    ['/payment/result'],
    ['/payment/abc123'],
  ])('%s 沒有頂部列', (path) => {
    expect(shouldShowTopBar(path)).toBe(false);
  });
});

describe('★ 返回的後備目標：直接打網址進來也要有地方去', () => {
  it('商品詳情 → /', () => {
    expect(backHrefFor('/products/abc123')).toBe('/');
  });

  it('購物車 → /', () => {
    expect(backHrefFor('/cart')).toBe('/');
  });

  it('結帳 → /cart（不是 /）：結帳的上一步就是購物車', () => {
    expect(backHrefFor('/checkout')).toBe('/cart');
  });

  it('沒有頂部列的頁面回 null——不畫的東西不需要目標', () => {
    expect(backHrefFor('/')).toBeNull();
    expect(backHrefFor('/orders')).toBeNull();
  });

  it('每一條規則都有非空的後備目標，而且是站內絕對路徑', () => {
    for (const rule of TOP_BAR_RULES) {
      expect(rule.backHref.startsWith('/')).toBe(true);
      expect(rule.backHref.startsWith('//')).toBe(false);
      expect(rule.backHref.length).toBeGreaterThan(0);
    }
  });

  it('每一條規則都寫得出理由', () => {
    for (const rule of TOP_BAR_RULES) {
      expect(rule.reason.length).toBeGreaterThan(10);
    }
  });
});

describe('購物車連結只放在商品詳情', () => {
  it('商品詳情有——加完購物車要看得到車', () => {
    expect(shouldShowCartLink('/products/abc123')).toBe(true);
  });

  it('購物車頁沒有——人已經在購物車了', () => {
    expect(shouldShowCartLink('/cart')).toBe(false);
  });

  it('結帳頁沒有', () => {
    expect(shouldShowCartLink('/checkout')).toBe(false);
  });
});

describe('漢堡鈕由 TOP_BAR_RULES 決定', () => {
  it('商品詳情與購物車顯示，結帳不顯示', () => {
    expect(shouldShowMenuButton('/products/abc123')).toBe(true);
    expect(shouldShowMenuButton('/cart')).toBe(true);
    expect(shouldShowMenuButton('/checkout')).toBe(false);
  });

  it('規則表逐條帶有明確布林值', () => {
    expect(TOP_BAR_RULES.map((rule) => [rule.route, rule.showMenuButton])).toEqual([
      ['/products/:productId', true],
      ['/cart', true],
      ['/checkout', false],
    ]);
  });
});

describe('路徑正規化與逐段比對', () => {
  it.each([
    ['/cart/', '結尾斜線'],
    ['/cart?from=home', '查詢字串'],
    ['/cart#top', 'hash'],
  ])('%s 仍然判成 /cart（%s）', (path) => {
    expect(shouldShowTopBar(path)).toBe(true);
    expect(backHrefFor(path)).toBe('/');
  });

  it('空字串與 / 都是首頁', () => {
    expect(normalizePathname('')).toBe('/');
    expect(normalizePathname('/')).toBe('/');
  });

  it('段數要一樣：/products 不會被 /products/:productId 吃掉', () => {
    expect(topBarRuleFor('/products')).toBeNull();
    expect(topBarRuleFor('/products/a/b')).toBeNull();
  });

  it('/cartfoo 不算 /cart——逐段比對，不是字串前綴', () => {
    expect(shouldShowTopBar('/cartfoo')).toBe(false);
  });
});

describe('isShellException', () => {
  it('/payment/:orderId 是例外（自動導轉綠界，放出口等於邀請人把付款丟在半路）', () => {
    expect(isShellException('/payment/ord_123')).toBe(true);
  });

  it('/payment/result 不是例外——它是流程終點，正需要出口', () => {
    expect(isShellException('/payment/result')).toBe(false);
  });

  it('其他頁面都不是例外', () => {
    expect(isShellException('/')).toBe(false);
    expect(isShellException('/cart')).toBe(false);
  });
});

describe('shouldUseHistoryBack：判斷失準時走後備目標，不是「按了沒反應」', () => {
  const origin = 'http://localhost:5002';

  it('站內上一頁 → 走 history.back()', () => {
    expect(
      shouldUseHistoryBack({ referrer: `${origin}/products`, origin, historyLength: 3 }),
    ).toBe(true);
  });

  it('★ 直接打網址（referrer 空的）→ 不攔，走 href', () => {
    expect(shouldUseHistoryBack({ referrer: '', origin, historyLength: 3 })).toBe(false);
  });

  it('★ 新分頁（history.length === 1）→ 不攔：back() 在那裡什麼都不會發生', () => {
    expect(
      shouldUseHistoryBack({ referrer: `${origin}/products`, origin, historyLength: 1 }),
    ).toBe(false);
  });

  it('跨站的上一頁 → 不攔：回去等於把人丟出站外', () => {
    expect(
      shouldUseHistoryBack({ referrer: 'https://line.me/x', origin, historyLength: 5 }),
    ).toBe(false);
  });

  it('同網域不同 port 也算跨站（origin 比的是 scheme+host+port）', () => {
    expect(
      shouldUseHistoryBack({ referrer: 'http://localhost:5003/', origin, historyLength: 5 }),
    ).toBe(false);
  });

  it('壞掉的 referrer 不猜，走後備目標', () => {
    expect(shouldUseHistoryBack({ referrer: 'not a url', origin, historyLength: 5 })).toBe(false);
  });

  it('origin 拿不到（非瀏覽器環境）→ 不攔', () => {
    expect(
      shouldUseHistoryBack({ referrer: `${origin}/products`, origin: '', historyLength: 5 }),
    ).toBe(false);
  });
});
