/**
 * 「登入之後回到原本要去的那一頁」的判斷邏輯。
 *
 * ── 為什麼是純函式住在 `_lib` ──
 * 跟 `tabs.ts`／`topBar.ts` 同一個理由：這個 workspace 沒有 jsdom 也沒有
 * `@testing-library`，渲染層測不到的東西只有先抽出來才測得到。而這一支
 * **特別**需要測試——它是一個開放式導向（open redirect）的入口。
 *
 * ── 威脅在哪 ──
 * `?next=` 的值來自網址，也就是來自任何一個能發連結給使用者的人。
 * 照字面 `router.push(searchParams.get('next'))` 的話，
 * `https://本站/login?next=https://evil.com/login` 會變成「在本站的網域上登入、
 * 然後被送到長得一模一樣的假站再登入一次」。這是釣魚最省力的一種做法，
 * 因為使用者看到的第一個網域是真的。
 *
 * 所以這裡**只放行站內絕對路徑**，其餘一律回 `DEFAULT_NEXT`：
 * 判斷失準的後果是「回到 `/me`」，永遠不會是「被送出站外」。
 * 白名單而不是黑名單，是因為黑名單要窮舉所有繞法（`//`、`/\`、`https:`、
 * `%09//`、全形空白……），而窮舉這件事沒有人做得完。
 */

/** 沒有 `next`、或 `next` 不可信時去哪。`/me` 是登入後的家。 */
export const DEFAULT_NEXT = '/me';

/**
 * 不准出現在路徑裡的字元：控制字元與所有空白。
 *
 * ⚠ 這一條不是潔癖。瀏覽器在解析 URL 時會**丟掉** tab（`\t`）、換行（`\n`）與
 * 歸位（`\r`），所以 `/\t/evil.com` 送到 `location` 會變成 `//evil.com`
 * ——一個能繞過「開頭不是 `//`」那條檢查的協定相對網址。
 */
const FORBIDDEN_CHARS = /[\s\u0000-\u001f\u007f]/;

/**
 * 把 `?next=` 的原始值收斂成一個**可以安全 push 的站內路徑**。
 *
 * 放行條件（全部要成立）：
 * - 是字串而且不是空的
 * - 以單一 `/` 開頭（`//evil.com` 是協定相對網址，會出站）
 * - 不含反斜線（`/\evil.com` 在部分瀏覽器等同 `//evil.com`）
 * - 不含控制字元或空白（見 `FORBIDDEN_CHARS`）
 *
 * `https://evil.com/x`、`javascript:alert(1)` 這類本來就不以 `/` 開頭，
 * 第一條就擋掉了。
 */
export function safeNext(raw: string | null | undefined): string {
  if (typeof raw !== 'string') return DEFAULT_NEXT;
  if (raw === '') return DEFAULT_NEXT;
  if (!raw.startsWith('/')) return DEFAULT_NEXT;
  if (raw.startsWith('//')) return DEFAULT_NEXT;
  if (raw.includes('\\')) return DEFAULT_NEXT;
  if (FORBIDDEN_CHARS.test(raw)) return DEFAULT_NEXT;
  return raw;
}

/**
 * 組一個「帶著回程」的網址。
 *
 * **進來的值先過一次 `safeNext`**：這樣即使呼叫端把使用者可控的字串直接丟進來，
 * 產出的連結也不會變成站外導向的載具。組出來的東西一定解得回一個站內路徑。
 */
function hrefWithNext(basePath: string, next: string): string {
  return `${basePath}?next=${encodeURIComponent(safeNext(next))}`;
}

/** 登入頁的網址，帶著登入完要回去的地方。 */
export function loginHref(next: string): string {
  return hrefWithNext('/login', next);
}

/**
 * 註冊頁的網址，帶著註冊完要回去的地方。
 *
 * 登入與註冊互連的那條連結要把 `next` 傳下去，否則「被彈到登入頁 → 我還沒有帳號 →
 * 註冊 → 被丟到 `/me`」，人就在最後一步弄丟了原本要去的地方。
 */
export function registerHref(next: string): string {
  return hrefWithNext('/register', next);
}

/**
 * 現在這一頁的「回程」字串：路徑加上查詢字串。
 *
 * `usePathname()` 不含查詢字串，`useSearchParams()` 只有查詢字串，
 * 被 401 彈走的頁面（例如 `/orders?status=Shipped`）要兩段合起來才回得到原處。
 */
export function currentNext(pathname: string, search: string): string {
  const query = search.startsWith('?') ? search : search ? `?${search}` : '';
  return safeNext(`${pathname}${query}`);
}
