/**
 * `?next=` 的安全性測試。
 *
 * ── 為什麼這一支要寫得比別的細 ──
 * `safeNext` 是全站唯一一個「把網址上的字串拿去導向」的地方。
 * 它一旦放行了站外目標，本站就變成釣魚站的跳板：使用者點的是真網域、
 * 在真網域上登入、然後被送到假站再登入一次。
 *
 * 所以下面的案例分成兩半，兩半都要有：
 * ① **不准放行**的（每一條都對應一種真實的繞法）
 * ② **必須放行**的（過度嚴格會讓 C 這件事整個失效，人永遠回不到原頁）
 */
import { describe, expect, it } from 'vitest';
import { DEFAULT_NEXT, currentNext, loginHref, registerHref, safeNext } from '../auth';

describe('safeNext：只放行站內絕對路徑', () => {
  it.each([
    ['//evil.com', '協定相對網址——瀏覽器會補上目前的 scheme 直接出站'],
    ['//evil.com/login', '同上，而且長得像本站的登入頁'],
    ['https://evil.com/x', '完整的站外網址'],
    ['http://evil.com', '同上，非 TLS'],
    ['javascript:alert(1)', '不是導向而是執行'],
    ['JavaScript:alert(1)', 'scheme 不分大小寫'],
    ['\\\\evil.com', '反斜線在部分瀏覽器等同 //'],
    ['/\\evil.com', '單斜線加反斜線，同上'],
    ['data:text/html,<script>', 'data URL'],
    ['', '空字串'],
    ['orders', '相對路徑——接在目前路徑後面，結果無法預測'],
    ['./orders', '同上'],
    ['../../etc', '同上'],
    ['/\tevil', 'tab 會被瀏覽器丟掉，剩下的可能變成別的東西'],
    ['/\n//evil.com', '換行同上，去掉之後就是協定相對網址'],
    ['/ /evil.com', '空白'],
  ])('safeNext(%o) 回 /me（%s）', (raw) => {
    expect(safeNext(raw)).toBe(DEFAULT_NEXT);
  });

  it.each([
    [null],
    [undefined],
  ])('safeNext(%o) 回 /me——沒帶 next 是常態，不是錯誤', (raw) => {
    expect(safeNext(raw)).toBe(DEFAULT_NEXT);
  });

  it.each([
    ['/checkout'],
    ['/me'],
    ['/orders'],
    ['/orders/abc?x=1'],
    ['/orders?status=Shipped&cursor=abc'],
    ['/payment/9f2c1a'],
    ['/'],
    ['/products/9f2c1a#spec'],
  ])('safeNext(%o) 原樣回傳——站內路徑不能被誤殺', (raw) => {
    expect(safeNext(raw)).toBe(raw);
  });

  it('預設目標就是 /me，不是 /orders——「我的」現在有家了', () => {
    expect(DEFAULT_NEXT).toBe('/me');
  });
});

describe('loginHref / registerHref：把回程帶著走', () => {
  it('loginHref 組出來的 next 解得回原本的路徑', () => {
    const href = loginHref('/checkout');
    expect(href).toBe('/login?next=%2Fcheckout');

    const parsed = new URL(href, 'https://example.test');
    expect(safeNext(parsed.searchParams.get('next'))).toBe('/checkout');
  });

  it('帶查詢字串的回程也解得回來——編碼過的 & 不會被當成另一個參數', () => {
    const href = loginHref('/orders?status=Shipped&cursor=abc');
    const parsed = new URL(href, 'https://example.test');

    expect(parsed.searchParams.get('cursor')).toBeNull();
    expect(safeNext(parsed.searchParams.get('next'))).toBe('/orders?status=Shipped&cursor=abc');
  });

  it('registerHref 同樣帶得動——「我還沒有帳號」那一步不能把回程弄丟', () => {
    const parsed = new URL(registerHref('/checkout'), 'https://example.test');
    expect(parsed.pathname).toBe('/register');
    expect(safeNext(parsed.searchParams.get('next'))).toBe('/checkout');
  });

  it('★ 站外目標進來也組不出站外連結——組網址這一步自己再擋一次', () => {
    expect(loginHref('https://evil.com/x')).toBe('/login?next=%2Fme');
    expect(registerHref('//evil.com')).toBe('/register?next=%2Fme');
  });
});

describe('currentNext：被 401 彈走時記得住現在在哪', () => {
  it.each([
    ['/orders', '', '/orders'],
    ['/orders', '?status=Shipped', '/orders?status=Shipped'],
    ['/orders', 'status=Shipped', '/orders?status=Shipped'],
    ['/me', '', '/me'],
  ])('currentNext(%o, %o) === %o', (pathname, search, expected) => {
    expect(currentNext(pathname, search)).toBe(expected);
  });

  it('組出來的東西也要過 safeNext——pathname 不是憑空來的，它來自網址', () => {
    expect(currentNext('//evil.com', '')).toBe(DEFAULT_NEXT);
  });
});
