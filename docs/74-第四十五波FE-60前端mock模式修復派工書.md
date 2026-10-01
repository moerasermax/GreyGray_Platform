# 第四十五波派工書 —— FE-60　前端 mock 模式修復（#66）（前端）

**給一個子代理。FE-60：讓 `NEXT_PUBLIC_USE_MOCK=1` 重新可用——前台與後台的請求在 mock 模式下都要被 MSW 攔到，後台 mock 模式能進到儀表板。正式 build（`NEXT_PUBLIC_USE_MOCK=0`）行為一律不變。**

- 計畫：後端 worktree 的 `docs/72-第四十五波計畫書.md`。#66 由使用者 2026-10-01 拍板「第四十五波順便修」。
- 為什麼要先修：本波 FE-58（後台取號資訊與人工退款）要先用 mock 做，後端要到後面才接上。
- **你不准改 `docs/` 底下任何檔。** 派工書以 `.dispatch/ACTIVE.md` 的 `doc:` 為準。

---

## 0. 事實（前端 HEAD 為準；對不上就停下來回報）

### 0.1 開關與技術

- 開關是環境變數 `NEXT_PUBLIC_USE_MOCK=1`（兩個 app 的 `.env.example` 第 3 行），沒有專門的 script。`NEXT_PUBLIC_*` 是**編譯期**內嵌：改了要重啟 dev，最好先清 `.next`（`GreyGray_PM/03-驗收紀錄.md` 第 808 行前例）。
- MSW 2.15.0、Next 15.5.24。SSR 側：`frontend/apps/storefront/instrumentation.ts` 第 13 行看開關、第 15 行載入 `frontend/apps/storefront/app/_mock/server.node.ts`（`msw/node` 的 `setupServer`）、第 16 行 `listen({ onUnhandledRequest: 'bypass' })`。瀏覽器側：`frontend/apps/storefront/app/_mock/MockBootstrap.tsx` 第 48～50 行 `storefrontWorker.start(...)`（worker 由 `packages/api-client/src/mock/browser.ts` 第 32 行的 `setupWorker` 建立），同樣 `'bypass'`。admin 對應：`frontend/apps/admin/app/_mock/MockBootstrap.tsx` 第 39、47～49 行。handler 在 `packages/api-client/src/mock/`。
- `apps/admin/middleware.ts` 第 94 行 `runtime: 'nodejs'`。

### 0.2 壞在哪

- **網址對不上（主因）**：handler 寫死 `localhost`——`handlers.storefront.ts` 第 34 行 `STOREFRONT_BASE_URL = 'http://localhost:5000'`；`handlers.admin.ts` 第 35 行、`handlers.admin.auth.ts` 第 21 行（另有 compensation 第 23 行、procurement 第 21 行、shipments 第 26 行三份未匯出的同值常數）都是 `'http://localhost:5001'`。但兩個 app 的 `frontend/apps/storefront/app/_lib/apiClient.ts` 與 `frontend/apps/admin/app/_lib/apiClient.ts` 第 17 行一律用 `NEXT_PUBLIC_API_BASE_URL`，而 `.env.local` 自 FE-12（約 2026-08-30）起是 `http://127.0.0.1:500x`。MSW 用完整網址比對（msw 套件原始碼 matchRequestUrl.ts 第 58～66 行），`'bypass'` 下請求就直接送去真後端。頁面開在 `127.0.0.1:500x`、API 打 `localhost:500x` 時，Service Worker 仍攔得到（`mockServiceWorker.js` 第 94～113 行沒有跨來源過濾）。
- **RSC 雜訊**：兩支 apiClient.ts 第 60～63 行在模組層級 `import('../_mock/MockBootstrap').then((m) => m.startMock())`；`MockBootstrap` 是 `'use client'`，在 server component 載入 apiClient.ts 時拿到的是 client reference，呼叫會在函式內的 `typeof window` 守衛（storefront 第 40 行）之前就丟錯、沒人接。只是 log 雜訊，但要清掉。
- **後台登入閘門**：`apps/admin/middleware.ts` 第 60 行只看 `gg_admin_session` cookie、第 64～66 行沒有就導 `/login`，**沒有 mock 分支**。mock 的登入 stub（`handlers.admin.ts` 第 77 行）不發 cookie；帶 Set-Cookie 的 `adminAuthHandlers`（第 437 行展開）排在後面、從沒生效，而且就算生效，MSW 在瀏覽器用 `document.cookie` 寫 HttpOnly cookie 會被瀏覽器忽略。結果 mock 登入後又被彈回 `/login`。
- 後台 `apps/admin/app` 範圍內只有 `frontend/apps/admin/app/_lib/apiClient.ts` 自己用 `serverApi`，後台資料全在瀏覽器端抓。

### 0.3 測試現況與既有狀態

- 沒有任何測試涵蓋 mock 切換；整個前端沒有測試用過 `vi.mock`、`vi.stubEnv`、`vi.resetModules` 或 `NextRequest`。可參考的先例：`apps/admin/app/login/_lib/session.auth-mock.test.ts` 第 11 行 `import { setupServer } from 'msw/node'`、第 15 行從 `@greygray/api-client/mock/handlers.admin.auth` 匯入 `ADMIN_BASE_URL`（`packages/api-client/package.json` 的 exports 已有 `"./mock/*"`，不用改 packages）。
- vitest 是 **node 環境、沒有 jsdom**。
- **既有狀態（FE-12 #10，不屬本包）**：現有 `NEXT_PUBLIC_USE_MOCK=0` 的正式 build，`.next/static` 本來就含 `setupWorker`／`mockServiceWorker` 字串（來源在 `frontend/apps/storefront/app/_mock/MockBootstrap.tsx` 的 lazy chunk 等 allow 之外的地方）。本包**不清**它們，只保證不新增。
- 正式建置實際的控制點是 後端樹的 ops/build-frontends.ps1第 128 行把 `NEXT_PUBLIC_USE_MOCK` 固定成 `'0'`。

---

## 1. 要做的事

1. **兩支 `frontend/apps/storefront/app/_lib/apiClient.ts`、`frontend/apps/admin/app/_lib/apiClient.ts`**：匯出 mock 網址常數（例如 `MOCK_API_BASE_URL`：前台 `'http://localhost:5000'`、後台 `'http://localhost:5001'`，字面值）；`process.env['NEXT_PUBLIC_USE_MOCK'] === '1'` 時 `BASE_URL` 用它，否則維持現狀（env 值，沒給時用原本的預設值）。**不准 import handler 檔**（會把 msw 打進正式 bundle）；兩處字面值相等由 T3 鎖住。
2. **兩支 apiClient.ts 第 60～63 行**：條件加上 `typeof window !== 'undefined'`，server 端（RSC 與 SSR）一律不 import `MockBootstrap`。
3. **`apps/admin/middleware.ts`**：`process.env['NEXT_PUBLIC_USE_MOCK'] === '1'` 時在檢查 cookie **之前**放行（mock 下固定是 Owner，由 `handlers.admin.ts` 第 79 行的 `/v1/me` stub 提供）。非 mock 行為一字不變。
4. **測試**（見 §3）。
5. 報告裡寫給 Leader 的 **mock 走查步驟**（你不准起 dev server），至少列出：① 後端不起；② shell 設 `NEXT_PUBLIC_USE_MOCK=1`（**不改 `.env.local`**），先移走 `.next`；③「算修好」的判準：server log 有 `[mock] … SSR server 已啟動` 且沒有 `Attempted to call startMock()`、商品頁 view-source 看得到 mock 商品名、瀏覽器 console 有 `[mock] … worker 已啟動` 而且 Network 沒有送往 `127.0.0.1:500x` 的請求、後台 `/login` 送出後停在儀表板；④ 提醒：node 端（SSR）與瀏覽器端的 mock 狀態是兩份記憶體，不要用「瀏覽器加購物車後看 SSR 頁」這類跨兩邊的比對當判準。

不做：handler 改讀 env、四角色切換、新增 `dev:mock` script（要裝 `cross-env`，屬人工閘門）、新增 mock 種子資料、清掉既有 bundle 裡的 mock 字串。

---

## 2. 你的 `allow`（`.dispatch/ACTIVE.md` 生效中的那份為準）

```
frontend/apps/storefront/app/_lib/apiClient.ts
frontend/apps/admin/app/_lib/apiClient.ts
frontend/apps/admin/middleware.ts
frontend/apps/storefront/app/_lib/__tests__/
frontend/apps/admin/app/_lib/__tests__/
.dispatch/reports/FE-60.md
```

不准改 `docs/`、`packages/`（含 handler 與 fixtures）、`.env*`、instrumentation.ts、`_mock/`、任何頁面。

---

## 3. 驗證要求

測試裡環境變數一律寫全名 `NEXT_PUBLIC_USE_MOCK`、`NEXT_PUBLIC_API_BASE_URL`；每個測試 `afterEach` 呼叫 `vi.unstubAllEnvs()`；`stubEnv` 後要 `vi.resetModules()` 再動態 import（模組層讀 env 的檔都一樣，含 middleware）。

| # | 內容 | 預期 | 怎麼驗 |
|---|---|---|---|
| T1 | 前台 mock 網址 | `NEXT_PUBLIC_USE_MOCK=1` 且 `NEXT_PUBLIC_API_BASE_URL=http://127.0.0.1:5000` 時，`serverApi()` 發出的請求被 `storefrontServer`（`onUnhandledRequest: 'error'`）攔到並成功；`=0` 時用 env 值 | 單元測試（`next/headers` 要 mock） |
| T2 | 後台 mock 網址 | 同 T1，後台版（`adminServer`） | 單元測試 |
| T3 | 字面值一致 | 前台 `MOCK_API_BASE_URL` 等於 `STOREFRONT_BASE_URL`；後台等於 `handlers.admin.ts` 第 35 行與 `handlers.admin.auth.ts` 第 21 行的 `ADMIN_BASE_URL`（另外三份未匯出的常數登記在「我發現但沒做的事」） | 單元測試（從 `@greygray/api-client/mock/*` 匯入常數） |
| T4 | middleware | mock：沒有 cookie 也放行 `/orders`（回應 header `x-middleware-next === '1'`）；非 mock：沒有 cookie 導 `/login`（307，`location` 依 middleware 現有格式，例如以 `/login?from=%2Forders` 結尾）、有 cookie 放行 | 單元測試（直接呼叫 middleware，用 `NextRequest`） |
| T5 | 型別與全套測試 | `pnpm typecheck` 全綠；`pnpm test` 全綠，報告「前 → 後」條數 | 指令輸出 |
| T6 | 正式 bundle 不新增 mock 相依（**基準比對**） | **動手前**先用 HEAD 跑兩個 app 的 `NEXT_PUBLIC_USE_MOCK=0` build，記下 `.next/static` 中含 `setupWorker`／`mockServiceWorker`／`localhost:500` 的檔案清單與 react-loadable-manifest.json 的 key；改完再 build，兩份清單相同（只多出既有 `MockBootstrap` lazy chunk 不算回歸），manifest 裡沒有以 app/_lib/apiClient.ts 為來源、指向 `_mock` 或 `mock/*` 的新項目；後台 middleware 的 server 產物裡搜不到 `NEXT_PUBLIC_USE_MOCK`（證明已在編譯期折成常數；產物路徑請確認後寫進報告） | build＋搜尋輸出 |
| T7 | server 端不載入 `MockBootstrap` | `vi.mock` 掉 `MockBootstrap`（mock factory 裡把旗標設 true），`NEXT_PUBLIC_USE_MOCK=1`、`resetModules` 後在 node 環境動態 import `apiClient`，旗標仍是 false；兩個 app 各一條，修改前紅、修改後綠 | 單元測試 |

`docs/45` 邊界六類：非法狀態（正式 build 誤帶 `NEXT_PUBLIC_USE_MOCK=1` 會繞過登入閘門——**實際控制點是 後端樹的 ops/build-frontends.ps1 第 128 行固定 `'0'`，T4 只鎖程式路徑**，報告照這樣寫）；其餘說明為何不適用。

前景跑 `pnpm typecheck`、`pnpm test`（全部套件）、`pnpm --filter @greygray/storefront build`、`pnpm --filter @greygray/admin build`。**跑 build 前確認 5002、5003 沒有 dev server（有就停下來回報，不要自己殺）**。admin 的 `next build` 在這台機器偶發 worker 崩潰（`0xC0000409`）：build 若以 `0xC0000409`／`ENOENT`／`ENOTEMPTY` 失敗，**同參數重跑一次**；第二次仍失敗才停下來，附兩次輸出。

---

## 4. 報告

`.dispatch/reports/FE-60.md`，三個標頭一字不差：`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。逐條自驗以本文件 §1、§3 為準，並附 §1 第 5 點的走查步驟。你不能宣告通過。

---

## 5. 停下來回報的情況

- §0 的事實和你讀到的檔案對不上
- 要讓測試通過必須改 allow 以外的檔（例如 handler、instrumentation.ts、`_mock/`）
- 需要安裝任何套件
