/**
 * 底部分頁列的「該不該顯示」與「現在停在哪一頁」——「現在卡在哪」#30 的迴歸測試。
 *
 * ── 為什麼這件事需要測試 ──
 * 「該不該顯示」本質上是一份**會漂移的路徑清單**：今天三頁用了 `BottomActionBar`，
 * 明天有人加第四頁，兩條固定列就疊在一起。清單漂移沒有任何編譯錯誤，
 * 只有在真瀏覽器裡連續操作才看得出來——#30 本身就是這樣活過八包、五波驗收的。
 *
 * ── 為什麼是純函式測試 ──
 * 這個 workspace 沒有 jsdom 也沒有 `@testing-library`，而且不打算為了測試加相依
 * （`packages/ui` 那次的教訓：加不了相依的地方，測試會變成休眠的）。
 * 所以判斷邏輯先抽成 `_lib/tabs.ts` 的純函式，這裡直接餵路徑字串。
 *
 * 另有兩個檔案從**別的方向**守同一件事：
 *   · `bottomActionBarCollision.test.ts` 去原始碼裡真的找誰用了 `BottomActionBar`
 *   · `tabBarReservesBottomSpace.test.ts` 比對分頁列高度與 `globals.css` 的留白
 */
import { describe, expect, it } from 'vitest';
import {
  STOREFRONT_TABS,
  TAB_BAR_RULES,
  activeTabHref,
  normalizePathname,
  shouldShowTabBar,
  tabBarRuleFor,
} from '../tabs';

describe('shouldShowTabBar：三頁固定底部列不可以再疊一條分頁列', () => {
  // 派工書 §1 的完整清單（Leader 已 grep 確認），逐條釘住。
  it.each([
    ['/products/9f2c1a', '商品詳情：AddToCartPanel 的 BottomActionBar'],
    ['/cart', '購物車頁：前往結帳'],
    ['/checkout', '結帳頁：送出訂單'],
  ])('%s 不顯示（%s）', (pathname) => {
    expect(shouldShowTabBar(pathname)).toBe(false);
  });

  it('每一條隱藏規則都寫得出理由——沒有理由的例外會被下一個人順手刪掉', () => {
    for (const rule of TAB_BAR_RULES) {
      expect(rule.reason.trim().length).toBeGreaterThan(0);
    }
  });
});

describe('shouldShowTabBar：逛得到東西的頁面都要有出口', () => {
  it.each([
    ['/', '首頁'],
    ['/campaigns', '開團列表'],
    ['/campaigns/abc123', '開團詳情——這裡也能加入購物車，更需要看得到購物車分頁'],
    ['/products', '商品列表'],
    ['/categories/kitchen', '分類頁'],
    ['/orders', '我的訂單'],
    ['/orders/abc123', '訂單詳情'],
    ['/wallet', '儲值金'],
    ['/addresses', '收件地址'],
  ])('%s 顯示（%s）', (pathname) => {
    expect(shouldShowTabBar(pathname)).toBe(true);
  });

  it('/products 是列表、/products/{id} 才是詳情——前綴不可以一起吃掉', () => {
    expect(shouldShowTabBar('/products')).toBe(true);
    expect(shouldShowTabBar('/products/9f2c1a')).toBe(false);
  });
});

describe('shouldShowTabBar：登入與註冊頁（這是判斷，不是預設值）', () => {
  /*
   * 判斷是**顯示**，理由是可達性而不是美觀：
   * `/orders`、`/wallet`、`/addresses` 三頁在 401 時都會 `router.replace('/login')`，
   * 所以未登入的訪客從首頁點「我的」**一步就會到 /login**。
   * 而 login 只連得到 register、register 只連得回 login——
   * 在那裡藏起分頁列，等於在一個一步就到得了的地方原地重演 #30。
   * 又因為用的是 `replace` 不是 `push`，被彈開的那一頁根本不在歷史裡，
   * 「上一頁」也不是可靠的出口。
   * 這兩頁沒有 `BottomActionBar`，所以「兩條固定列疊在一起」那條硬限制不適用。
   */
  it.each([['/login'], ['/register']])('%s 顯示', (pathname) => {
    expect(shouldShowTabBar(pathname)).toBe(true);
  });
});

describe('shouldShowTabBar：付款流程', () => {
  it('/payment/{orderId} 不顯示——那一頁會自動 POST 導轉綠界，中途切走等於把付款丟在半路', () => {
    expect(shouldShowTabBar('/payment/ord_123')).toBe(false);
  });

  it('/payment/result 顯示——它是流程終點，而且沒有固定底部列', () => {
    expect(shouldShowTabBar('/payment/result')).toBe(true);
  });

  it('規則順序有意義：/payment/result 必須排在 /payment/:orderId 前面', () => {
    const routes = TAB_BAR_RULES.map((rule) => rule.route);
    expect(routes.indexOf('/payment/result')).toBeLessThan(routes.indexOf('/payment/:orderId'));
  });
});

describe('路徑正規化與逐段比對', () => {
  it.each([
    ['/cart/', '結尾斜線'],
    ['/cart?from=home', '查詢字串'],
    ['/cart#top', 'hash'],
  ])('%s 仍然判成 /cart（%s）', (pathname) => {
    expect(shouldShowTabBar(pathname)).toBe(false);
  });

  it.each([
    ['', '/'],
    ['/', '/'],
    ['//', '/'],
    ['/campaigns/', '/campaigns'],
  ])('normalizePathname(%o) === %o', (input, expected) => {
    expect(normalizePathname(input)).toBe(expected);
  });

  it('前綴比對是逐段的：/carts 與 /checkout-help 不是那三頁', () => {
    expect(shouldShowTabBar('/carts')).toBe(true);
    expect(shouldShowTabBar('/checkout-help')).toBe(true);
  });

  it('沒有規則命中時預設顯示——新頁面自動有出口，那正是 #30 要修的事', () => {
    expect(tabBarRuleFor('/some/brand/new/page')).toBeNull();
    expect(shouldShowTabBar('/some/brand/new/page')).toBe(true);
  });
});

describe('四個分頁', () => {
  it('就是使用者拍板的四個：首頁 · 開團 · 購物車 · 我的', () => {
    expect(STOREFRONT_TABS.map((tab) => tab.label)).toEqual(['首頁', '開團', '購物車', '我的']);
  });

  it('每個分頁都指到真的存在的前台路徑', () => {
    expect(STOREFRONT_TABS.map((tab) => tab.href)).toEqual(['/', '/campaigns', '/cart', '/me']);
  });

  it('★「我的」指到 /me 而不是 /orders——訂單只是它底下的一頁，不是帳號區的家', () => {
    const account = STOREFRONT_TABS.find((tab) => tab.label === '我的');
    expect(account?.href).toBe('/me');
    // `/orders` 沒有消失，它退回成 `alsoActiveFor`：人在訂單頁時「我的」仍然要亮。
    expect(account?.alsoActiveFor).toContain('/orders');
  });

  it('/me 有分頁列——它是帳號區的家，走得進去也要走得出來', () => {
    expect(shouldShowTabBar('/me')).toBe(true);
  });

  it('首頁上找得到「購物車」——#30 使用者撞到的正是「首頁連這三個字都沒有」', () => {
    expect(shouldShowTabBar('/')).toBe(true);
    expect(STOREFRONT_TABS.some((tab) => tab.label === '購物車' && tab.href === '/cart')).toBe(true);
  });
});

describe('activeTabHref：現在停在哪一個分頁', () => {
  it.each([
    ['/', '/'],
    ['/products', '/'],
    ['/products/9f2c1a', '/'],
    ['/categories/kitchen', '/'],
    ['/campaigns', '/campaigns'],
    ['/campaigns/abc123', '/campaigns'],
    ['/cart', '/cart'],
    ['/checkout', '/cart'],
    ['/me', '/me'],
    ['/orders', '/me'],
    ['/orders/abc123', '/me'],
    ['/wallet', '/me'],
    ['/addresses', '/me'],
    ['/login', '/me'],
    ['/register', '/me'],
  ])('%s 亮的是 %s', (pathname, expected) => {
    expect(activeTabHref(pathname)).toBe(expected);
  });

  it("'/' 只吃精確比對——拿它當前綴的話每一頁都會亮首頁", () => {
    expect(activeTabHref('/campaigns')).toBe('/campaigns');
    expect(activeTabHref('/orders')).toBe('/me');
  });

  it('逐段比對也適用於 /me：/medical 不算在「我的」底下', () => {
    expect(activeTabHref('/medical')).toBeNull();
  });

  it('對不上任何分頁時回 null，而不是硬亮一個', () => {
    expect(activeTabHref('/payment/result')).toBeNull();
  });

  it('逐段比對：/campaignsfoo 不算在 /campaigns 底下', () => {
    expect(activeTabHref('/campaignsfoo')).toBeNull();
  });
});
