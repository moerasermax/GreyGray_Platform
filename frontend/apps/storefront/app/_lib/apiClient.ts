/*
 * 前台的 `ApiClient` 唯一建立點。
 *
 * `packages/api-client` 的 `endpoints/*` 每一支都收 `client` 當第一個參數，
 * 但**誰來建那個 client** 原本沒有人負責。五個人各自 `new ApiClient(...)` 的話，
 * baseUrl、cookie 轉發、重試次數就會出現五種寫法，而其中四種沒有測試。
 * 一律從這裡拿。
 *
 * ── 為什麼 server 與 browser 要分兩支 ──
 * server component 沒有瀏覽器那層自動帶 cookie 的行為，session 要自己轉發，
 * 否則 SSR 出來的頁面永遠是「未登入」的樣子，而瀏覽器接手後又突然變成已登入，
 * 畫面會閃一下。這是 SSR 最常見也最難查的一種 bug。
 */
import { ApiClient } from '@greygray/api-client';

/** BFF 位址。mock 開著（`NEXT_PUBLIC_USE_MOCK=1`）時不會真的打出去。 */
const BASE_URL = process.env['NEXT_PUBLIC_API_BASE_URL'] ?? 'http://localhost:5000';

/**
 * **Server component／route handler 用。**
 * 會把瀏覽器帶進來的 cookie 原封不動轉發給 BFF。
 *
 * ```ts
 * const api = await serverApi();
 * const product = await getProduct(api, productId);
 * ```
 */
export async function serverApi(): Promise<ApiClient> {
  // `next/headers` 只能在 server 端 import，放在函式裡才不會被打包進 client bundle。
  const { cookies } = await import('next/headers');
  const jar = await cookies();
  const cookie = jar
    .getAll()
    .map((c) => `${c.name}=${c.value}`)
    .join('; ');

  // tsconfig 開了 exactOptionalPropertyTypes，headers 不能傳 undefined 進去。
  return cookie
    ? new ApiClient({ baseUrl: BASE_URL, headers: { cookie } })
    : new ApiClient({ baseUrl: BASE_URL });
}

/**
 * **Client component 用。** 瀏覽器自己會帶 cookie，不需要轉發。
 *
 * 每次呼叫回同一個實例——`ApiClient` 沒有請求層級的狀態，
 * 重複 new 只是浪費，而且會讓「同一把冪等鍵」這件事更難推理。
 */
let browserSingleton: ApiClient | null = null;

/*
 * mock 模式下，第一個請求要等 msw 的 service worker 接手。
 *
 * 不等的話會賽跑，而輸的一方拿到的是 ERR_CONNECTION_REFUSED（後端根本沒起），
 * 頁面就停在「連線失敗」——實測第二波的購物車／結帳／儲值金三頁固定踩中，
 * 按重試才會好。這是整包 mock 開發流程的地基，不是某一頁的問題。
 *
 * `NEXT_PUBLIC_USE_MOCK` 是編譯期字面值，關掉時整段會被搖掉，正式環境沒有成本。
 */
const mockReady =
  process.env['NEXT_PUBLIC_USE_MOCK'] === '1'
    ? import('../_mock/MockBootstrap').then((m) => m.startMock())
    : undefined;

export function browserApi(): ApiClient {
  browserSingleton ??= mockReady
    ? new ApiClient({ baseUrl: BASE_URL, ready: mockReady })
    : new ApiClient({ baseUrl: BASE_URL });
  return browserSingleton;
}
