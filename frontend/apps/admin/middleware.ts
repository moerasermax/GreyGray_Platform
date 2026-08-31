/**
 * 進站前的第一道閘門：只檢查 `gg_admin_session` 這個 HttpOnly cookie**存不存在**，
 * 不驗證它是不是還有效（過期／被撤銷這些要打 `GET /v1/me` 才知道，那是
 * `(dash)/layout.tsx` 與各頁面既有的錯誤處理在做的事，不是這裡的責任）。
 *
 * 這裡跑在 middleware（server 端），不是瀏覽器 JS，讀得到 HttpOnly cookie
 * 是正常的——「access token 永不進瀏覽器」講的是前端 JS（`document.cookie`），
 * 不是伺服器端。
 *
 * 沒有 cookie 就導去 `/login?from=<原本要去的路徑>`，登入成功後導回去。
 *
 * **已知問題，交付時要回報**：這個檔案一存在，`next build` 就會炸——
 * `instrumentation.ts` 掛的 MSW SSR bootstrap（`app/_mock/server.node.ts`）
 * 透過動態 import 被拉進 Next 的 Edge 編譯圖，而它最終依賴 `@mswjs/interceptors/ClientRequest`
 * 這個 Node-only 的 subpath，Edge runtime 解析不到，build 直接失敗
 * （`Module not found: Package path ./ClientRequest is not exported`，
 * import trace 指到 `./app/_mock/server.node.ts` → `./instrumentation.ts`）。
 * `instrumentation.ts` 裡已經有 `NEXT_RUNTIME !== 'nodejs'` 的執行期守衛，
 * 但 webpack 建 Edge 編譯圖時要先解析得到動態 import 的目標才能做 tree-shaking，
 * 守衛擋不住「先解析、後消除」這個順序，跟 middleware 存不存在無關的檔案本身沒問題，
 * 是兩份既有基礎設施（instrumentation.ts 的 MSW bootstrap ＋ 這一包新增的 middleware.ts）
 * 第一次同時出現才炸出來的。修法要動 `instrumentation.ts` 或 `next.config.ts`，
 * 兩個都不是這一包的檔案，不在這裡動手，留給整合者。
 */
import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

const SESSION_COOKIE = 'gg_admin_session';
const PUBLIC_PATHS = ['/login'];

function isPublicPath(pathname: string): boolean {
  return PUBLIC_PATHS.some((path) => pathname === path || pathname.startsWith(`${path}/`));
}

/**
 * standalone server.js 固定帶 HOSTNAME 環境變數，導致 Next 內部一律用
 * HOSTNAME:PORT 組 request.url 的 origin，完全不理會實際的反向代理 Host
 * header（即使開了 experimental.trustHostHeader 也一樣，見 docs/22 §0）。
 * 正式機用 cloudflared 反代，這裡改成自己讀 x-forwarded-host／host header
 * 手動組 origin，不依賴 Next 那套機制。
 *
 * protocol 不受同一個問題影響（X-Forwarded-Proto 已經被 Next 正確反映在
 * nextUrl.protocol 上，FE-18／FE-12 都驗證過），所以只覆寫 host 這一段。
 */
function resolveOrigin(request: NextRequest): string {
  const forwardedHost = request.headers.get('x-forwarded-host') ?? request.headers.get('host');
  if (!forwardedHost) {
    return request.nextUrl.origin;
  }
  return `${request.nextUrl.protocol}//${forwardedHost}`;
}

export function middleware(request: NextRequest): NextResponse {
  const { pathname, search } = request.nextUrl;

  if (isPublicPath(pathname)) {
    return NextResponse.next();
  }

  if (request.cookies.has(SESSION_COOKIE)) {
    return NextResponse.next();
  }

  const loginUrl = new URL('/login', resolveOrigin(request));
  loginUrl.searchParams.set('from', `${pathname}${search}`);
  return NextResponse.redirect(loginUrl);
}

export const config = {
  // 排除 Next 內部路徑、mock service worker 檔案與常見靜態資源；其餘一律過閘門。
  matcher: ['/((?!_next/|api/|mockServiceWorker\\.js|favicon\\.ico|.*\\.(?:svg|png|jpg|jpeg|webp)$).*)'],

  /*
   * 跑 Node 而不是 Edge。**這一行是 build 過不過的關鍵**（整合驗收時加的）。
   *
   * middleware 一存在，Next 就會另外建一份 Edge 編譯圖，而 `instrumentation.ts`
   * 的動態 import 會把 `msw/node` → `@mswjs/interceptors/ClientRequest` 拉進去。
   * 那是 Node-only 的 subpath，Edge 解析不到，`next build` 直接失敗。
   * `instrumentation.ts` 裡的 `NEXT_RUNTIME !== 'nodejs'` 守衛擋不住——
   * webpack 要先解析得到目標才能 tree-shake，順序是「先解析、後消除」。
   * `next.config.ts` 的 `serverExternalPackages` 也救不了，那個只作用在 Node 那份圖。
   *
   * 選 Node runtime 而不是去 stub 那個模組，理由是這個專案自架在單一台機器上
   * （ADR-002／003），**根本沒有 edge 網路**，Edge runtime 的好處一項都用不到；
   * 而 webpack alias 那種修法只在 webpack build 生效、turbopack dev 不吃，
   * 會製造 dev 與 build 行為不一致——`next.config.ts` 的註解記著這個專案
   * 已經被同一類問題咬過一次了。
   *
   * 寫成純字面值，不要加 `as const`——Next 是靜態解析這個 `config` 匯出的，
   * 遇到 TsConstAssertion 會直接放棄整個 config
   * （`Unsupported node type "TsConstAssertion" at "config.runtime"`），
   * 於是 matcher 與 runtime 兩個都不生效。
   */
  runtime: 'nodejs',
};
