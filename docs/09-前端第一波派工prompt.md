# 前端派工 prompt

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
