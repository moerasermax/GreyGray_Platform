# 前端派工 prompt

（檔名還叫「第一波」是歷史原因，內容已涵蓋全部波次。檔名不改，別處有引用。）

**工作目錄是 `GreyGray_Platform-fe\`（有 `-fe` 的那個）。**
沒有 `-fe` 的是後端的工作區，Codex 在裡面跑，你這棵樹看不到它。

---

## 第 0 則：前端主 agent 的啟動 prompt（**開 terminal 先貼這則**）

貼給前端那個 Claude terminal。它不自己寫頁面，它負責**指揮子 agent ＋ 驗收**。

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
你是 GreyGray Platform 的**前端主 agent**。你不自己寫元件與頁面，
你的工作是：派工給子 agent → 收回交付 → 驗收 → 決定下一波。

【先讀，全部讀完再動手】
  frontend/README.md            四條規則
  docs/06-前端工作包.md          八包的規格、檔案所有權表、工作區說明
  docs/09-前端第一波派工prompt.md ← 你在這裡。每一波的子 agent prompt 都在裡面
  docs/05-API契約.md             前後端唯一的邊界，已凍結
  docs/api/openapi.*.yaml        端點與型別

【三條鐵則】
1. **一次只開一個波次。** 全部交付並驗收通過，才開下一波。
   不要因為某一包早交了就先放下一包進來——那會讓「整包退回」變成做不到的事。
2. **檔案所有權表就是邊界**（docs/06）。子 agent 回報要改共用檔時由你決定，
   不要讓它自己改。
3. **commit 只准包含 frontend/ 底下的路徑。** 看到 src/、ops/、.github/
   出現在變更清單裡，就是走錯工作區了。

【怎麼派】
本檔每一波都有現成的子 agent prompt，**原文複製**，一個子 agent 一則。
不要自己改寫——裡面每一句都是踩過坑才寫上去的。

【怎麼驗收——子 agent 說「完成」不算完成】
你要自己跑，而且**貼出實際輸出**，不是「已完成」四個字：

  pnpm typecheck          全部專案必須綠
  pnpm build              兩個 app 必須綠
  git status              清單裡只准有 frontend/ 底下的檔案
  git diff --stat         比對所有權表，有沒有越界

外加該包在 docs/06 的「驗收」段落逐條檢查。
**任何一條不過就整包退回，不做部分接受。**

驗收通過後把結果寫進本檔末尾的「驗收紀錄」，再開下一波。

【什麼時候停下來問人】
- 契約缺欄位、型別對不上 → **不要自己在前端補**，回報。
  契約是凍結的，改它要走 docs/05 的流程。
- 所有權表沒寫到的檔案 → 回報。
- packages/ui/src/tokens/** —— 分兩種，不要一律回報：
  * **新增** token（補一個原本沒有的角色）→ 你可以決定，但交付說明要列出
    新增了什麼、為什麼原本的不夠用，並附實測對比度數字。
  * **修改既有 token 的值** → 一定回報。既有值一動，所有已經做完的畫面
    都會跟著變，而那些畫面是別人驗收過的。

  （第一波實例：後台缺「疊在有色底上的文字」這個角色，--ga-fg-muted 只在
  --ga-bg 上量過，放到 danger-subtle 上只有 3.90:1。主 agent 新增
  --ga-fg-on-tint 與 --ga-primary-text 解決，沒動任何既有值——那是對的做法。）
```

---

# 第一波：FE-1 / FE-2 / FE-6

**三則各自複製，貼給三個子 agent，可以同時開。**
FE-1 的 mock 是所有人的資料來源，FE-2、FE-6 是所有頁面的元件底座，
這三個沒好就往下做，等於在流沙上蓋樓。

---

## 第一則：FE-1　型別產生、mock server 與端點層

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                     四條規則
  docs/06-前端工作包.md                   ← 主文件。通用前綴、工作區說明、
                                           檔案所有權表、你那一包的細節都在裡面
  docs/05-API契約.md                     前後端唯一的邊界，已凍結
  docs/api/openapi.storefront.yaml       前台 28 個操作
  docs/api/openapi.admin.yaml            後台 40 個操作

【這次的範圍】
docs/06-前端工作包.md 的 **FE-1，就這一包**。
看到 FE-2 ～ FE-8 的任何東西都不要碰，那是別人正在同時做的。

你獨佔的路徑（只准改這些）：
  packages/api-client/src/types.*.ts
  packages/api-client/src/mock/**
  packages/api-client/src/endpoints/**

【依賴已經幫你裝好了，不要跑 pnpm install】
  msw 2.15 與 vitest 3.2 已經加進 packages/api-client 的 devDependencies，
  `pnpm --filter @greygray/api-client test` 這條 script 也已經在了。
  **package.json 與 pnpm-lock.yaml 一律不准碰**——另外兩個 agent 正在同一棵樹上工作，
  你跑 install 會動到他們腳下的 node_modules。缺套件就停下來回報。

【特別注意】
1. `pnpm api:generate` 已經跑得通、型別也已經產出來了（storefront 1,776 行／
   admin 2,106 行）。你的工作是確認它們是最新的，**不是重新設定它**。
2. 你的 mock 是 FE-3 ～ FE-8 全部人的資料來源。假資料要「像真的」——
   中文藥妝品名、真實台幣金額（amountMinor 是分，NT$780 就是 78000）、
   混合現貨與預購的購物車。用「商品1 商品2」做出來的畫面看不出版面問題。
3. **每個端點都要有錯誤 fixture**：至少一個 422（帶 errors 欄位驗證）、一個 409、
   一個 500。前端最常漏的就是錯誤畫面，沒有 fixture 就不會有人做。
4. 分頁 mock 要真的能翻到第二頁，最後一頁回 nextCursor: null。
   只回一頁的 mock 會讓無限捲動的 bug 拖到上線才出現。
5. mock 要能用 NEXT_PUBLIC_USE_MOCK=1 開關，後端好了之後不改任何頁面程式碼就能切過去。
6. **「掛載」不歸你。** msw 在瀏覽器要 public/mockServiceWorker.js ＋ app/layout.tsx 初始化，
   SSR 要另一組 setupServer——那些都是無主共用檔，**你不要碰**。
   你的交付到 packages/api-client/src/mock/ 為止：匯出瀏覽器端與 node 端兩個進入點，
   然後在回報裡寫清楚「整合的人要在哪個檔案加哪幾行、要跑哪個 msw init 指令」。
   前台商品頁走 SSR（FE-3），所以 node 端那條路徑不能省。
7. 契約裡**沒有 operationId**，函式名要你自己取。交付時附一張
   「HTTP 方法 ＋ 路徑 → 函式名」對照表，第二波五個人要照著接。
8. tsconfig.base.json 開了 exactOptionalPropertyTypes、noUncheckedIndexedAccess、
   verbatimModuleSyntax。配 openapi-typescript 產的型別會很難纏，那是刻意的，
   **不要去改 tsconfig**（也是無主檔案）。

【交付前一定要做】
  pnpm --filter @greygray/api-client typecheck     # 必須綠
  pnpm --filter @greygray/api-client test          # smoke test 必須綠
  git status                                       # 清單裡只准有 frontend/ 底下的檔案
看到 src/、ops/、.github/ 出現在變更清單裡，代表你走錯工作區了，停下來回報。
package.json 或 pnpm-lock.yaml 出現在清單裡也一樣，停下來回報。

交付時附上：跑過的指令與實際輸出、你動過的檔案清單、
以及**你認為契約有問題的地方**（不要默默在前端補，那是最貴的技術債）。
```

---

## 第二則：FE-2　Soft Seoul 元件庫

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

前台的視覺是韓系柔美「Soft Seoul」（ADR-009 已定案，不要重新提案風格）。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                          四條規則
  docs/06-前端工作包.md                        ← 主文件，你那一包的元件清單在裡面
  frontend/packages/ui/src/tokens/soft-seoul.css   設計 token，
                                              **檔頭寫了為什麼是這些值，一定要看**
  docs/00-decisions.md 的 ADR-009             前台風格的決定與理由

【這次的範圍】
docs/06-前端工作包.md 的 **FE-2，就這一包**。

你獨佔的路徑（只准改這些）：
  packages/ui/src/components/**
  packages/ui/src/index.ts

已預先授權的例外（就這一個）：
  apps/storefront/app/kitchen-sink/page.tsx —— 驗收用的展示頁，**只准新建這一個檔案**。
  docs/06 寫的是 `app/_kitchen-sink/`，那是錯的：底線開頭在 App Router 是 private folder，
  不會產生路由，做出來打不開。**以這裡的路徑為準。**
  這頁是暫時的，第二波由 FE-3 刪掉。

**元件清單就是文件裡那張表，不要多做。** 多做的沒有人會用，還會擋到別人。

【特別注意】
1. **顏色與尺寸只准從 token 取。** 元件裡不准出現 raw hex，
   不准 rounded-[18px] 這種任意值。平行開發時這條特別要緊——
   四個人各自挑一個粉紅色，出來就是四個產品。
2. Soft Seoul 的粉紅拆成三個**不可互換**的角色，用錯就是無障礙不合格：
     --gg-pink-decor   #EC4899  只給漸層與裝飾，上面不可以放小字（白字只有 3.53:1）
     --gg-primary      #DB2777  互動元件底色，白字 4.60:1 ✓
     --gg-primary-text #BE185D  淺底上的粉紅文字與連結，6.04:1 ✓
3. **前台刻意不做深色模式**（token 檔頭有寫理由）。不要順手加。
4. 動態一律 150–300ms，用 token 的 --gg-duration-* 與 --gg-ease-*。
   **不要引入 GSAP**——M1a 的動態 CSS transition 就夠，多一個 60KB 的函式庫不划算。
5. 不要用 emoji 當圖示。**圖示一律 inline SVG，不准加任何圖示套件**——
   package.json 是無主共用檔，而且另外兩個 agent 正在同一棵樹上工作，
   你跑 pnpm install 會動到他們腳下的 node_modules。
   自己在 components/icons/ 底下放一組，風格統一。
6. `PriceDisplay` 內部呼叫 formatMoney()，禁止呼叫端自己格式化。
   `Skeleton` 要保留與實際內容相同的高度，否則載入完會跳版。
   `Countdown` 吃後端給的 closesAt，但能不能下單看 isAcceptingOrders。
7. 只在真的需要互動時才加 'use client'。

【交付前一定要做】
  pnpm --filter @greygray/ui typecheck         # 必須綠
  pnpm --filter @greygray/storefront typecheck # kitchen sink 頁也要綠
  git status                                   # 清單裡只准有 frontend/ 底下的檔案
                                               # 出現任何 package.json 就是走錯了
外加 docs/06 的 FE-2 驗收四條：kitchen sink 頁、375/768/1024/1440 四個寬度無水平捲動、
鍵盤走得完且每一步看得到焦點框、內文對比度 ≥ 4.5:1。

交付時附上：跑過的指令與實際輸出、動過的檔案清單、三個寬度的截圖。
```

---

## 第三則：FE-6　後台殼、登入與營運儀表板

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

後台是中性、密集的儀表板，跟前台的 Soft Seoul 是兩套完全不同的東西，不要混用。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                       四條規則
  docs/06-前端工作包.md                     ← 主文件，你那一包的元件清單在裡面
  frontend/packages/ui/src/tokens/admin.css     後台 token，檔頭寫了為什麼
  docs/api/openapi.admin.yaml              後台 40 個操作，注意每個端點的 x-required-role

【這次的範圍】
docs/06-前端工作包.md 的 **FE-6，就這一包**。
FE-7、FE-8 依賴你做出來的殼與元件，但那是下一波，你不要做他們的頁面。

你獨佔的路徑（只准改這些）：
  packages/ui/src/admin/**
  apps/admin/app/(dash)/layout.tsx
  apps/admin/app/(dash)/page.tsx
  apps/admin/app/login/**

已預先授權的例外（就這一個，而且是必做）：
  **刪除 apps/admin/app/page.tsx**（現有的骨架佔位頁，檔頭自己寫了「會被 FE-6 換掉」）。
  它與你要建的 (dash)/page.tsx 都解析到 `/`，兩個並存 Next.js 直接 build fail。
  不刪你的 build 驗收不會綠。這是唯一准你動的所有權表外檔案。

【特別注意】
1. **首頁最重要的一塊是「負債 vs 現金」**（GET /v1/ledger/liability-vs-cash）。
   isBreached = true 代表正在用還沒交貨的錢過日子——這是代購生意最典型的崩壞前兆。
   要放在進來第一眼就看得到的位置，**不要塞進第三個分頁**。
2. **後台要做深色模式**（對帳常常是晚上的事）。token 已經備好，用 data-theme 切換。
   淺色與深色兩套都要跑對比度，正文 ≥ 4.5:1。
3. 金額欄位一律走 MoneyCell（tabular-nums、右對齊），禁止直接印字串。
   位數對不齊時，掃一整欄要逐格重新對焦，對帳的人會恨你。
4. DirectionCell（借／貸）**顏色之外一定要有文字**。只靠顏色的話色盲使用者讀不出來。
5. 角色檢查：用端點的 x-required-role 決定選單顯不顯示。
   **但前端隱藏只是體驗，不是安全**——真正的檢查在後端，不要因為前端擋了就假設安全。
6. DataTable 在 20 列與 0 列都要正常，0 列是 EmptyState 不是一片空白。
7. FE-1 的 mock 還沒好（他跟你同時在跑）。你就是要用自己的暫時假資料把畫面做出來，
   但**不要把假資料寫進 packages/api-client**（那是 FE-1 的地盤），
   放在自己的路由資料夾底下，交付時列出來。
8. **不要加任何套件、不要跑 pnpm install。** package.json 是無主共用檔，
   而且另外兩個 agent 正在同一棵樹上工作。圖示一律 inline SVG。缺什麼停下來回報。
9. tsconfig.base.json 開了 exactOptionalPropertyTypes、noUncheckedIndexedAccess、
   verbatimModuleSyntax，型別會比你習慣的嚴。那是刻意的，**不要改 tsconfig**。

【交付前一定要做】
  pnpm --filter @greygray/ui typecheck
  pnpm --filter @greygray/admin typecheck
  pnpm --filter @greygray/admin build
  git status                                # 清單裡只准有 frontend/ 底下的檔案
                                            # 除了刪掉 app/page.tsx 之外不該有別的越界

交付時附上：跑過的指令與實際輸出、動過的檔案清單、
淺色與深色兩套的截圖、以及你認為契約有問題的地方。
```

---

## 收到交付之後

三包都回來了再一起驗，不要一包一包接受——元件庫與 mock 的問題往往要到
第二波接起來才看得出來。驗收看 `docs/06-前端工作包.md` 各包的「驗收」段落，
外加共通的三條：

1. `git status` / `git diff --stat` 有沒有越出所有權表
2. `pnpm typecheck`、`pnpm test` 與 `pnpm build` 全綠
   （貼實際輸出，不是「已完成」四個字。**三道都要在 workspace 根目錄跑**，
   不要只跑 `--filter` 某一個套件——第一波就是這樣漏掉一個紅的）
3. 三個人回報的「契約有問題的地方」要合在一起看——
   同一個欄位被兩個人各自獨立提出來，那就不是誤會，是契約真的有洞

---

# 第二波：FE-3 / FE-4 / FE-5 / FE-7 / FE-8

**五則各自複製，貼給五個子 agent，可以同時開。**
第一波（mock ＋ 兩套元件庫 ＋ 後台殼）已經驗收通過，這五包站在上面。
FE-3～FE-5 共用前台元件庫，FE-7／FE-8 共用後台殼——**元件庫是唯讀的**，
缺東西要回報給主 agent，不要自己往 `packages/ui` 加。

## 派工前主 agent 已經做掉的（子 agent 不用重做）

- 刪掉 `apps/storefront/app/page.tsx`（骨架佔位頁）。它與 FE-3 的
  `(shop)/page.tsx` 都解析到 `/`，兩個並存 Next.js 直接 build fail。
  第一波 FE-6 就是被後台那份咬到，這次先拆掉，免得五個人同時 build 一起紅。
- 新增 `apps/storefront/app/_lib/apiClient.ts` 與 `apps/admin/app/_lib/apiClient.ts`。
  `endpoints/*` 每一支都收 `client` 當第一個參數，但原本沒有人負責建那個 client。
  **五包一律從這裡拿，不要自己 `new ApiClient(...)`。**
- msw 已經掛好（service worker ＋ client bootstrap ＋ SSR instrumentation），
  `NEXT_PUBLIC_USE_MOCK=1` 就會生效，兩個方向都實測過。

---

## 第四則：FE-3　前台：逛與找

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

前台的視覺是韓系柔美「Soft Seoul」（ADR-009 已定案，不要重新提案風格）。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                              四條規則
  docs/06-前端工作包.md                            ← 主文件，FE-3 那一段是你的規格
  docs/05-API契約.md                              前後端唯一的邊界，已凍結
  docs/api/openapi.storefront.yaml                前台 28 個操作
  packages/api-client/src/endpoints/README.md     ← 路徑 → 函式名對照表，照這張接
  frontend/packages/ui/src/index.ts               第一波做好的元件庫（唯讀）
  apps/storefront/app/kitchen-sink/page.tsx       全部元件的用法示範，開 dev 看得到

【這次的範圍】
docs/06 的 **FE-3，就這一包**：首頁 · 分類頁 · 商品列表 · 商品詳情 · 開團列表 · 開團詳情。
FE-4（購物車與結帳）、FE-5（會員與訂單）正在同時做，看到那些東西都不要碰。

你獨佔的路徑（只准改這些）：
  apps/storefront/app/(shop)/**

已預先授權的例外（就這一個）：
  你可以在 `(shop)/` 底下自由建 `_components/`、`_lib/` 子資料夾放頁面專屬的東西。
  **不要放進 packages/ui**，那是元件庫，這一波是唯讀的。

【環境：兩件事先做】
1. `cp apps/storefront/.env.example apps/storefront/.env.local`
   （`.env.local` 不進版控，所以你的樹裡還沒有。沒有它 mock 不會開。）
2. `pnpm dev:storefront` 起在 :5002。**dev script 已經帶了 --turbopack，不要拿掉**
   ——webpack 的 dev 編譯會讓 msw 的 SSR 掛載整站 500，而 build 反而是過的。

【特別注意】
1. **商品頁與商品列表走 SSR**（server component 直接呼叫 BFF），
   SEO 與 LINE 分享的 OG 標籤靠它。用 `await serverApi()` 拿 client。
   互動的部分（收藏、加購）才切 client component 並用 `browserApi()`。
2. **預購商品的 `available` 恆為 0，但仍然可以下單。** 預購是訂單驅動，
   下單只登記需求。拿 `available` 擋預購是這包最容易犯的錯。
3. **「這個團還能不能下單」讀 `isAcceptingOrders`**，不要拿 `closesAt` 跟現在時間比。
   客戶端時鐘不可信，截團是後端的 Saga Timer 說了算。`Countdown` 元件吃 `closesAt`
   只負責顯示倒數，能不能按是另一件事。
4. 列表用游標式分頁，`nextCursor` 為 `null` 時停止。**不要顯示總筆數**，後端不給。
5. enum 一律容忍未知值：`switch` 要有 `default`，顯示原始字串，不要當成錯誤。
   後端新增狀態不算破壞性變更。
6. 每個商品配 1–2 句描述、顯示單位價格、統一 1:1 裁切。
   單位價格直接用後端的 `unitPriceLabel` 字串，**不要自己算**——
   「／32 顆」要知道 SKU 包裝數，那是後端才有的資料。
7. 首頁結構照 ADR-009：圓角搜尋列 ＋ 頭像 → 柔粉漸層 banner ＋ 圓形產品 →
   橫捲圓形分類標 → 商品卡牆（帶 NEW／人氣標籤與收藏愛心）。

【元件庫是唯讀的】
`packages/ui` 缺你要的元件時：先確認不是命名不同（看 kitchen-sink 頁），
真的缺就做在 `(shop)/_components/` 底下，並在交付時列出來。
**不要改 packages/ui、不要改 tokens、不要改任何 package.json。**

已知的兩個偏離，**不要自己修**（要修是主 agent 的事，你回報就好）：
  - `ProductCard` 用原生 `<img>` 不是 `next/image`（packages/ui 沒有 next 依賴）
  - `Dialog`／`BottomSheet` 沒有用 `createPortal`，父層有 `overflow:hidden`
    或 CSS transform 會裁切它

【交付前一定要做——三道都在 workspace 根目錄跑】
  cd frontend
  pnpm typecheck     # 4 個專案全綠
  pnpm test          # 全綠
  pnpm build         # 兩個 app 全綠
  git status         # 清單裡只准有 frontend/apps/storefront/app/(shop)/ 底下的檔案

typecheck 報 `.next/types/... Cannot find module '.../page.js'` 是刪過路由之後的
快取殘留，`rm -rf apps/*/.next` 再跑一次就好，不是你的錯。

外加 docs/06 的 FE-3 驗收三條：
  - 商品詳情頁 `view-source` 看得到商品名與描述（證明是 SSR 不是 CSR）
  - 列表滾到底會載入第二頁，最後一頁不再打 API
  - 一個已截團的團：商品還看得到，但加購鈕是 disabled 並說明原因

【做完就停】
上面三道指令綠了、驗收三條檢查過了，**就停下來回報，不要往下做別包**。
FE-4／FE-5 有人在做。你把 `(shop)/` 做完就是完成，不要因為「順手」去補別的。

【回報格式】
  1. 我建立／修改／刪除的檔案清單（完整路徑）
  2. 我跑過的指令與**實際輸出**（貼原文，不要寫「已完成」四個字）
  3. 我做了哪些假設
  4. **我認為契約有問題的地方**（缺欄位、型別對不上、語意不清）
  5. 我想要但 packages/ui 沒有的元件（做在自己資料夾的那些）
  6. 還沒做完或刻意沒做的事
```

---

## 第五則：FE-4　前台：買

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

前台的視覺是韓系柔美「Soft Seoul」（ADR-009 已定案，不要重新提案風格）。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                              四條規則
  docs/06-前端工作包.md                            ← 主文件，FE-4 那一段是你的規格
  docs/05-API契約.md                              **§冪等鍵那一節要讀熟，這包全靠它**
  docs/api/openapi.storefront.yaml                cart / checkout / payment 的完整型別
  packages/api-client/src/endpoints/README.md     路徑 → 函式名對照表
  packages/api-client/src/idempotency.ts          `createIdempotencyScope` 的用法
  apps/storefront/app/kitchen-sink/page.tsx       元件用法示範

【這次的範圍】
docs/06 的 **FE-4，就這一包**：購物車 · 配送方式選擇與詢價 · 結帳 · 付款導轉 · 付款結果頁。
FE-3（逛與找）、FE-5（會員與訂單）正在同時做，看到那些東西都不要碰。

你獨佔的路徑（只准改這些）：
  apps/storefront/app/(checkout)/**

【環境：兩件事先做】
1. `cp apps/storefront/.env.example apps/storefront/.env.local`
2. `pnpm dev:storefront` 起在 :5002。**dev script 的 --turbopack 不要拿掉。**

【特別注意——這包是整個前台最容易出錢的問題的地方】
1. **冪等鍵。** 結帳表單載入時用 `createIdempotencyScope()` 建一把，
   整個送出流程（含重試）共用同一把，成功之後才 `reset()`。
   **按鈕連點兩下必須是同一把 key**——換 key 重試會變成第二張訂單，
   客人被扣兩次錢。這是這包最重要的一條。
2. **一律顯示含運總額**（`quote.grandTotal`），不是商品小計。
   還沒選配送方式前顯示「運費另計」，**不要顯示 0**——客人會以為免運。
3. 切換配送方式就重新 `POST /v1/cart/quote`。那個端點是純函式、不寫入，
   可以放心多打。含運總額一律用後端回的數字，**前端不做任何金額加總**。
4. **混合訂單**（`hasMixedModes = true`）必須讓客人選 `shippingPolicy`，
   而且要說清楚兩個選項的差別：「現貨先出會付兩次運費／等回國一起出省一次」。
   這是下單時就要問的，不是出貨時。
5. **付款導轉**：拿 `PaymentInitiation` 建一個 hidden form，把 `fields`
   **原封不動**（連順序都不要動）POST 到 `action`。
   不要自己組簽章、不要改任何欄位的值、不要少送欄位——綠界會驗簽，改了就失敗。
6. `explain` 陣列要顯示出來（可摺疊）。客服要能直接回答「為什麼收這麼多」。
7. 購物車有一項帶 `availabilityWarning` 時，結帳鈕要被擋下並說明原因。
8. 網路中斷要顯示 `NetworkError` 的訊息，不是白畫面。用元件庫的 `ErrorState`。

【元件庫是唯讀的】
缺元件就做在 `(checkout)/_components/` 底下並在交付時列出來。
**不要改 packages/ui、不要改 tokens、不要改任何 package.json。**

已知的兩個偏離，**不要自己修**，回報就好：
  - `ProductCard` 用原生 `<img>` 不是 `next/image`
  - `Dialog`／`BottomSheet` 沒有用 `createPortal`，父層有 `overflow:hidden`
    或 CSS transform 會裁切它。**你這包會大量用到 BottomSheet（配送方式選單），
    遇到被裁切不要自己在 packages/ui 動手，回報給主 agent。**

【交付前一定要做——三道都在 workspace 根目錄跑】
  cd frontend
  pnpm typecheck && pnpm test && pnpm build
  git status         # 清單裡只准有 apps/storefront/app/(checkout)/ 底下的檔案

外加 docs/06 的 FE-4 驗收四條：
  - 連點結帳鈕五次，只產生一張訂單（mock 要能驗證 `Idempotency-Key` 相同）
  - 切換超商／宅配／自取，含運總額跟著變，且都是後端回的數字
  - 購物車有一項 `availabilityWarning` 時，結帳鈕被擋下並說明原因
  - 網路中斷模擬：顯示 `NetworkError` 的訊息，不是白畫面

**第一條請寫成可重複執行的驗證**（一段腳本或一個測試），不要只是手點五次說沒事。

【做完就停】
三道指令綠了、驗收四條檢查過了，**就停下來回報，不要往下做別包**。

【回報格式】
  1. 檔案清單　2. 指令與實際輸出　3. 假設　4. 契約問題
  5. 缺的元件　6. 沒做完或刻意沒做的
```

---

## 第六則：FE-5　前台：我的

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

前台的視覺是韓系柔美「Soft Seoul」（ADR-009 已定案，不要重新提案風格）。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                              四條規則
  docs/06-前端工作包.md                            ← 主文件，FE-5 那一段是你的規格
  docs/00-decisions.md 的 ADR-019                 不遷移歷史會員與訂單，這條決定了註冊頁的文案
  docs/05-API契約.md                              前後端唯一的邊界，已凍結
  docs/api/openapi.storefront.yaml                auth / me / orders 的完整型別
  packages/api-client/src/endpoints/README.md     路徑 → 函式名對照表
  apps/storefront/app/kitchen-sink/page.tsx       元件用法示範

【這次的範圍】
docs/06 的 **FE-5，就這一包**：註冊 · 登入 · 我的訂單 · 訂單詳情 · 收件地址 CRUD · 儲值金餘額。
FE-3（逛與找）、FE-4（購物車與結帳）正在同時做，看到那些東西都不要碰。

你獨佔的路徑（只准改這些）：
  apps/storefront/app/(account)/**

【環境：兩件事先做】
1. `cp apps/storefront/.env.example apps/storefront/.env.local`
2. `pnpm dev:storefront` 起在 :5002。**dev script 的 --turbopack 不要拿掉。**

【特別注意】
1. **註冊表單的欄位集中在一處**（一個 schema 檔），不要散在多個元件裡。
   欄位本身已經定案（ADR-019），不會再變；集中的理由是
   **Google 帳號串接排在 M1a 之後**，屆時要在同一個註冊流程裡插入 OAuth 的入口，
   散在多個元件會很難改。
2. **老客人會發現自己要重新註冊、看不到舊訂單**（歷史資料不遷移，ADR-019）。
   註冊頁要有一句話說明「舊訂單請到原平台查詢」，不要讓人以為資料弄丟了。
   **期限的日期還沒確定**，文案先寫成「期限請見公告」之類不會寫死日期的說法，
   並在交付時列出來提醒主 agent 之後要補。
3. **登入失敗一律顯示「手機號碼或密碼錯誤」**，不要區分帳號不存在與密碼錯——
   區分了等於送對方一份帳號列舉工具。後端也守著這條，前端不要自作聰明還原細節。
4. **訂單狀態有九個，畫一條時間軸。`Cancelled` 不在主線上**，要另外處理。
   `switch` 一定要有 `default` 並顯示原始字串（後端新增狀態不算破壞性變更）。
5. 訂單詳情要顯示 `paymentDueAt` 倒數（逾期未付會自動取消），
   但**倒數歸零不要自己把畫面改成已取消**——重新 `GET` 拿後端的狀態。
   客戶端時鐘不可信，狀態是後端說了算。
6. **缺貨退款的 line 要看得出來**：`status = Unavailable` ＋ `refundedAmount`，
   並說明「其餘品項照常出貨」。客人最怕的是以為整張單沒了。
7. 儲值金餘額旁要說明「退款選儲值金零手續費、下次可直接折抵」。
8. 表單驗證錯誤（422）的 `errors` 要對應到正確的欄位下方，不要全部塞在最上面。

【元件庫是唯讀的】
缺元件就做在 `(account)/_components/` 底下並在交付時列出來。
**不要改 packages/ui、不要改 tokens、不要改任何 package.json。**

已知的兩個偏離，**不要自己修**，回報就好：
  - `ProductCard` 用原生 `<img>` 不是 `next/image`
  - `Dialog`／`BottomSheet` 沒有用 `createPortal`

【交付前一定要做——三道都在 workspace 根目錄跑】
  cd frontend
  pnpm typecheck && pnpm test && pnpm build
  git status         # 清單裡只准有 apps/storefront/app/(account)/ 底下的檔案

外加 docs/06 的 FE-5 驗收三條：
  - 九個訂單狀態各有一組 fixture，時間軸都畫得出來
  - 塞一個 mock 回未知的 `status` 值，畫面顯示原始字串而不是崩掉
  - 表單驗證錯誤（422）的 `errors` 對應到正確的欄位下方

【做完就停】
三道指令綠了、驗收三條檢查過了，**就停下來回報，不要往下做別包**。

【回報格式】
  1. 檔案清單　2. 指令與實際輸出　3. 假設　4. 契約問題
  5. 缺的元件　6. 沒做完或刻意沒做的（含註冊頁那句待補的期限文案）
```

---

## 第七則：FE-7　後台：商品與開團

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

後台是中性、密集的儀表板，跟前台的 Soft Seoul 是兩套完全不同的東西，不要混用。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                              四條規則
  docs/06-前端工作包.md                            ← 主文件，FE-7 那一段是你的規格
  docs/05-API契約.md                              前後端唯一的邊界，已凍結
  docs/api/openapi.admin.yaml                     後台 40 個操作，注意每個端點的 x-required-role
  packages/api-client/src/endpoints/README.md     路徑 → 函式名對照表
  frontend/packages/ui/src/admin/index.ts         第一波做好的後台元件庫（唯讀）
  apps/admin/app/(dash)/layout.tsx                第一波做好的殼，你的頁面掛在它底下
  apps/admin/app/(dash)/page.tsx                  儀表板，看它怎麼用 DataTable 與 MoneyCell

【這次的範圍】
docs/06 的 **FE-7，就這一包**：分類 · 商品列表／建檔／編輯（含 SKU）·
開團列表／建檔／編輯 · 開團商品 · 狀態操作。
FE-8（訂單與帳務）正在同時做，`orders/` 與 `ledger/` 都不要碰。

你獨佔的路徑（只准改這些）：
  apps/admin/app/(dash)/catalog/**
  apps/admin/app/(dash)/campaigns/**

【環境：兩件事先做】
1. `cp apps/admin/.env.example apps/admin/.env.local`
2. `pnpm dev:admin` 起在 :5003。**dev script 的 --turbopack 不要拿掉。**
   後台目前是假登入（`app/login/_lib/session.ts`），
   帳號 `owner@greygray.tw` 密碼 `greygray123`。

【特別注意】
1. **商品建檔的 `weightGram` 與 `size` 設成必填。**
   M1a 運費一口價用不到，但 M3 啟用材積重計費時資料就已經在那裡——
   回頭補幾百筆商品尺寸是純粹的浪費。
   **表單上要寫一句話說明為什麼現在要填**，否則使用者會覺得多此一舉而亂填。
2. **開團的四個狀態操作（publish / close / cancel / settle）各有前置條件，
   不要在前端判斷**——照後端回的 `status` 決定按鈕能不能按，
   422 的訊息直接顯示給使用者。前端猜業務規則遲早會跟後端對不起來。
3. `cancel` 要二次確認，且確認文案要說明
   「該團全部訂單會取消退款，但已登錄的旅程成本仍然留在帳上」。
4. **`sellingPrice` 發布後不可改**，UI 要反映這件事（發布後欄位變唯讀）。
   理由：開團時定死售價是「逾時視為照買」能成立的前提。
5. 角色檢查：用端點的 `x-required-role` 決定選單與按鈕顯不顯示。
   **但前端隱藏只是體驗，不是安全**——真正的檢查在後端。
6. 金額欄位一律走 `MoneyCell`（tabular-nums、右對齊），禁止直接印字串。
7. `DataTable` 在 20 列與 0 列都要正常，0 列是 `EmptyState` 不是一片空白。
8. enum 一律容忍未知值，`switch` 要有 `default`。

【元件庫是唯讀的】
`packages/ui/src/admin` 缺你要的元件時：先確認不是命名不同（看 `(dash)/page.tsx`），
真的缺就做在 `catalog/_components/` 或 `campaigns/_components/` 底下並列出來。
**不要改 packages/ui、不要改 tokens、不要改任何 package.json。**

顏色一律用後台 token 的語意 class（`text-fg`、`text-fg-muted`、`text-fg-on-tint`、
`bg-surface`…）。**疊在有色底（`*-subtle`）上的文字一律用 `text-fg-on-tint`**，
不要用 `text-fg-muted`——它只在 `--ga-bg` 上被量過，放到有色底上過不了 4.5:1。
這是第一波驗收實測出來的，不要重蹈。

【交付前一定要做——三道都在 workspace 根目錄跑】
  cd frontend
  pnpm typecheck && pnpm test && pnpm build
  git status   # 清單裡只准有 apps/admin/app/(dash)/{catalog,campaigns}/ 底下的檔案

外加 docs/06 的 FE-7 驗收三條：
  - 建一個團 → 加商品 → 發布 → 截團，四步都走得通（mock）
  - 對已發布的團嘗試改售價，欄位是唯讀的
  - `settle` 在訂單未全部出貨時回 422，訊息完整顯示

淺色與深色兩套都要看過，正文對比度 ≥ 4.5:1。

【做完就停】
三道指令綠了、驗收三條檢查過了，**就停下來回報，不要往下做別包**。
FE-8 在做 `orders/` 與 `ledger/`，那不是你的。

【回報格式】
  1. 檔案清單　2. 指令與實際輸出　3. 假設　4. 契約問題
  5. 缺的元件　6. 沒做完或刻意沒做的
```

---

## 第八則：FE-8　後台：訂單與帳務

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe\frontend
GreyGray Platform 前端。Next.js 15 App Router ＋ React 19 ＋ Tailwind v4，pnpm workspace。
這是前端專用的 git worktree（分支 feat/frontend-wave-1）。後端在另一個工作區平行進行，
兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到任何後端程式碼。

後台是中性、密集的儀表板，跟前台的 Soft Seoul 是兩套完全不同的東西，不要混用。

【先讀，不要跳過，讀完再動手】
  frontend/README.md                              四條規則
  docs/06-前端工作包.md                            ← 主文件，FE-8 那一段是你的規格
  docs/05-API契約.md                              前後端唯一的邊界，已凍結
  docs/api/openapi.admin.yaml                     orders / ledger 的完整型別
  packages/api-client/src/endpoints/README.md     路徑 → 函式名對照表
  frontend/packages/ui/src/admin/index.ts         第一波做好的後台元件庫（唯讀）
  apps/admin/app/(dash)/_components/LiabilityVsCashCard.tsx
                                                  負債 vs 現金已經做好了，你要做的是
                                                  帳務區底下的完整版，不要重寫它

【這次的範圍】
docs/06 的 **FE-8，就這一包**：訂單列表（搜尋／狀態／團篩選）· 訂單詳情 ·
整張取消 · 單品項取消 · 分錄查詢 · 每團毛利 · 負債 vs 現金。
FE-7（商品與開團）正在同時做，`catalog/` 與 `campaigns/` 都不要碰。

你獨佔的路徑（只准改這些）：
  apps/admin/app/(dash)/orders/**
  apps/admin/app/(dash)/ledger/**

【環境：兩件事先做】
1. `cp apps/admin/.env.example apps/admin/.env.local`
2. `pnpm dev:admin` 起在 :5003。**dev script 的 --turbopack 不要拿掉。**
   假登入帳號 `owner@greygray.tw` 密碼 `greygray123`。

【特別注意——這包碰的是錢與個資，兩件事都不能將就】
1. **訂單詳情要能一眼看出每個 line 的狀態不同**——缺貨的、已出貨的、
   待採購的會同時存在於同一張訂單。不要只顯示訂單層級的狀態。
2. **取消時 `refundTo` 兩個選項要說明差別**：
   原路退回有手續費、**退成儲值金完全不動金流零手續費**。**預設選儲值金。**
3. **客戶聯絡方式預設遮罩。** 要看明文得另外操作並填寫存取理由，
   每一次讀取都會寫進 audit。**UI 要讓人知道這件事，不要偷偷記**——
   畫面上要寫明「查看明文會被記錄」。
4. **分錄畫面沒有編輯也沒有刪除。** 分錄一經 posted 即不可修改，
   更正只能開反向分錄。**這不是遺漏，UI 上要主動說明**，
   否則使用者會一直找編輯鈕，最後跑去改資料庫。
5. **每團毛利的 `grossMargin` 是後端算好的，不要自己加減六個欄位再對答案。**
   前端算了，帳就有兩個來源，而其中一個永遠沒有測試。
6. 金額欄位一律走 `MoneyCell`（tabular-nums、右對齊），禁止直接印字串。
   位數對不齊時掃一整欄要逐格重新對焦，對帳的人會恨你。
7. `DirectionCell`（借／貸）**顏色之外一定要有文字**。只靠顏色色盲讀不出來。
8. `DataTable` 在 20 列與 0 列都要正常，0 列是 `EmptyState` 不是一片空白。
9. enum 一律容忍未知值，`switch` 要有 `default`。

【元件庫是唯讀的】
缺元件就做在 `orders/_components/` 或 `ledger/_components/` 底下並列出來。
**不要改 packages/ui、不要改 tokens、不要改任何 package.json。**

顏色一律用後台 token 的語意 class。**疊在有色底（`*-subtle`）上的文字
一律用 `text-fg-on-tint`**，不要用 `text-fg-muted`——它只在 `--ga-bg` 上被量過。
這是第一波驗收實測出來的，不要重蹈。

【交付前一定要做——三道都在 workspace 根目錄跑】
  cd frontend
  pnpm typecheck && pnpm test && pnpm build
  git status   # 清單裡只准有 apps/admin/app/(dash)/{orders,ledger}/ 底下的檔案

外加 docs/06 的 FE-8 驗收三條：
  - 一張含「已出貨 ＋ 缺貨退款 ＋ 待採購」三種 line 的訂單顯示正確
  - 分錄列表的借貸兩欄合計相等（fixture 就要是平的）
  - 聯絡方式預設是遮罩的，且畫面上寫明「查看明文會被記錄」

淺色與深色兩套都要看過，正文對比度 ≥ 4.5:1。

【做完就停】
三道指令綠了、驗收三條檢查過了，**就停下來回報，不要往下做別包**。
FE-7 在做 `catalog/` 與 `campaigns/`，那不是你的。

【回報格式】
  1. 檔案清單　2. 指令與實際輸出　3. 假設　4. 契約問題
  5. 缺的元件　6. 沒做完或刻意沒做的
```

---

## 第二波收到交付之後

五包都回來了再一起驗，不要一包一包接受。共通四條：

1. `git status` / `git diff --stat` 有沒有越出所有權表。
   **注意：所有權表是「同一波之內」的邊界，不是跨波凍結。**
   第二波為了接線而改到第一波的檔案是正常的，要擋的是同一波兩個人互蓋。
2. `pnpm typecheck`、`pnpm test`、`pnpm build` 全綠，**三道都在 workspace 根目錄跑**。
3. 五個人回報的「契約有問題的地方」合在一起看——
   同一個欄位被兩個人各自獨立提出來，那就不是誤會，是契約真的有洞。
4. 五個人回報的「packages/ui 缺的元件」合在一起看——
   同一個元件被兩個人各自做在自己的資料夾裡，那就該升上元件庫。

視覺驗收（真的開瀏覽器，不要只信回報）：
  - 前台 375 / 768 / 1024 / 1440 四個寬度無水平捲動
  - 鍵盤走得完，每一步看得到焦點框
  - 後台淺色與深色兩套，正文對比度 ≥ 4.5:1
  - 第一波的教訓：量 token 值只能證明「意圖」，要量**實際渲染出來的組合**。
    `outline-width` 有設但 `outline-style: none` 一樣畫不出來。

---

# 驗收紀錄

## 第一波（FE-1 / FE-2 / FE-6）—— 2026-08-28 通過

驗 `a9e1636`。機械驗收由 Claude 在乾淨 worktree 獨立跑過一次，
修掉一個缺陷後在 `-fe` 樹上再跑一次確認。

```
pnpm install --frozen-lockfile   ✅
pnpm typecheck                   ✅ 4 個專案
pnpm test                        ✅ api-client 27 條（admin smoke 13 ＋ storefront smoke 14）
pnpm build                       ✅ storefront: / · /_not-found · /kitchen-sink
                                    admin:      / · /_not-found · /login
git status                       ✅ 乾淨
```

**修掉的缺陷**：`pnpm test` 在 workspace 根目錄是紅的。
兩個 app 的 `test` script 是 `vitest run`，但沒有測試檔，vitest 直接 `exit 1`，
連帶讓 `pnpm --recursive test` 整個失敗。主 agent 回報「全綠」是因為它跑的是
`--filter @greygray/api-client test`。已改成 `vitest run --passWithNoTests`。

**兩個查證後沒事的**：
- `--gg-sheet-max-h` 在 diff 裡看起來被刪，實際是在檔案內搬位置，
  `soft-seoul.css` 仍定義、`BottomSheet.tsx` 仍使用。
- `money.ts` 只改檔頭文件，行為零變化（並把「台幣經 ICU 是 `$` 不是 `NT$`」寫對）。

**所有權**：六類無主檔全被動到（`layout.tsx`／`globals.css`／`next.config.ts`／
`package.json`／`tokens/**`／`money.ts`），但全部由主 agent 在收尾 commit 改，
且每一項都附了理由。token 是純新增 6 個、沒動任何既有值。
這正是升級路徑該有的樣子——規則已據此改成「新增可自決、改既有值要回報」。

**沒有獨立重跑的**：瀏覽器視覺驗收（四寬度無水平捲動、33 站鍵盤焦點、
兩套主題對比度、msw 端到端）。那些是主 agent 跑真瀏覽器做的，有數字有記錄，
採信但未重做。

**留給第二波的一題**：契約的 `unitPriceLabel` 範例寫 `NT$780／32 顆`，
但共用的 `formatMoney` 對台幣輸出 `$780`（zh-TW 是台幣本地語系，ICU 就給 `$`）。
同一張商品卡上會同時出現兩種寫法。**這是產品決定，不是前端能自己定的**——
要嘛契約改成 `$`，要嘛 `formatMoney` 對 TWD 特別加 `NT$` 前綴。

---

## 第二波（FE-3 / FE-4 / FE-5 / FE-7 / FE-8）—— 2026-08-28 通過

驗 `898787e`（交付 `d849b13` ＋ 主 agent 自己的整合驗收 `898787e`），
由 Claude 在 detached worktree 獨立跑，不在前端 agent 的工作樹上
（驗收當下它還有 19 個未提交檔案，正在做第三批修補）。

```
pnpm install --frozen-lockfile   ✅ 54.4s
pnpm typecheck                   ✅ 4 個專案
pnpm test                        ✅ 71 條（api-client 31／storefront 40）
pnpm build                       ✅ 兩個 app
```

測試從第一波的 27 條長到 71 條。**FE-3～FE-5 補了 40 條 storefront 測試**，
這是第一波沒有的——第一波兩個 app 都是零測試檔。

### 實際產出的路由

**storefront 18 條**：`/` · `/products` · `/products/[productId]` ·
`/categories/[categoryId]` · `/campaigns` · `/campaigns/[campaignId]`（FE-3）／
`/cart` · `/checkout` · `/payment/[orderId]` · `/payment/result` ·
`/mock-cashier`（FE-4）／ `/login` · `/register` · `/orders` ·
`/orders/[orderId]` · `/addresses` · `/wallet`（FE-5）

**admin 13 條**：`/` · `/login`（FE-6 第一波）／ `/catalog` ·
`/catalog/categories` · `/catalog/products/[productId]` ·
`/catalog/products/new` · `/campaigns` · `/campaigns/[campaignId]` ·
`/campaigns/new`（FE-7）／ `/orders` · `/orders/[orderId]` · `/ledger`（FE-8）

`/kitchen-sink` 已如計畫由 FE-3 刪除。

### 一個要補的

**admin 到現在還是零測試檔**（`No test files found`）。
FE-7 與 FE-8 交付了 7 條路由但沒有任何測試，而 FE-3～FE-5 補了 40 條。
後台的商品建檔、開團狀態操作、訂單取消都是會改資料的操作，
下一波要求 FE-7／FE-8 補上——不然 `--passWithNoTests` 會一直掩蓋這件事。

### 我沒有獨立重跑的

瀏覽器視覺驗收。主 agent 在 `898787e` 說它「修掉四個只有把包接起來才看得到的
缺陷」，那類問題只有真的跑起來才驗得到，我採信它的紀錄但沒有重做。
