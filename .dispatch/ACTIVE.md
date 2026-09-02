# 生效中的派工

**這個檔案是閘門的唯一輸入。** 只有寫在這裡的 `allow:` 路徑才准寫入原始碼。
由**整合者**維護；實作者模式下閘門會擋住對這個檔案本身的寫入，
所以實作者不能自己擴大授權。

格式：

```
package: <包名>
doc: <派工書路徑>
allow: <相對 repo 根的路徑前綴，一行一個>
```

沒有任何 `package:` 行 = 整合者模式，原始碼一律不准寫。
**註解掉（`<!-- -->`）與圍欄（```）裡的內容不會被讀進來**，可以安心放範例。

---

## 開工：你只開一個 terminal

**Leader 模型。** 使用者開一個 terminal 當 Leader，Leader 用 ai-cli fan out 子代理，
一包一個子代理。使用者不必一包一包開 terminal，也不必自己設 `GG_PACKAGE`。

**Leader 的啟動 prompt 與八包的原文都在 `.dispatch/PROMPTS.md`。**

### 三種身分，閘門分得出來

| 身分 | 怎麼宣告 | 寫得了什麼 |
|---|---|---|
| **Leader** | prompt 開頭 `GG_ROLE=leader`，或 `GG_ROLE=leader claude` | 閘門檔（`.dispatch/`、`.claude/`、`.codex/`）、`docs/`、`GreyGray_PM`。**不寫原始碼** |
| **實作者** | prompt 開頭 `GG_PACKAGE=<包名>`，或 `GG_PACKAGE=<包名> codex` | 只有該包 `allow:` 的路徑，加上全域放行的 `docs/` 等 |
| **身分不明** | 沒宣告 | **什麼都寫不了**（有派工生效時）。這是刻意的 fail-closed |

第三列是重點：**「忘記宣告的實作者」與「Leader」從外面看一模一樣**，
所以不能用「沒綁包別」推定是 Leader——那會讓忘記宣告的人擁有改閘門的權力。
Leader 要明講。

> **包別優先於角色。** 子代理的 prompt 一定帶 `GG_PACKAGE=`；
> 萬一同一段文字裡也混進 `GG_ROLE=leader`（例如 Leader 把整份說明貼過去），
> 子代理仍然只會被綁成實作者，不會升級。已實測。

> 為什麼子代理不能用環境變數：ai-cli 的 `run` 只吃 `workFolder`／`prompt`／`model`／
> `reasoning_effort`／`session_id`，**沒有 env 參數**；而且它的子行程是
> `env: process.env`（`src/core/process-service.ts`），繼承的是 MCP server 自己的環境。
> 一個 server 行程 spawn 所有子代理，行程層級的環境變數本質上帶不了
> 「每個子代理不同」的值。所以綁定只能走 prompt ＋ session_id。

一個 session 只認第一次宣告，之後想改包或改角色都會被拒絕。
包名拼錯一律擋下（fail-closed）。

---

## 生效中：FE-26　跟上 ADR-030 契約（重生型別、拿掉 `!`）＋ #36 前端側 ＋ FE-25 ⑦ ＋ 結帳頁登入回跳保留已填內容

使用者 2026-09-02 親自走旅程第一張單（純預購）就撞到 #37：結帳 500、比登入檢查還早。拍板「用第二種方式修」→ ADR-030：
規則的主人是後端。後端 BE-41 已改契約（`shippingPolicy` 混合才必填、否則可 `null`）並推導；Leader 已把後端樹的
`docs/api/openapi.storefront.yaml` 與 `docs/05-API契約.md` 逐位元複製進這棵樹。這一包：`pnpm api:generate` 重生型別、
拿掉 `checkout/page.tsx` 的 `shippingPolicy!`；登出後 `publishCart(null)`（#36 前端側）；`/login?next=` 時分頁列亮 `next` 所屬分頁（FE-25 ⑦）；
結帳頁被帶去登入再回來時用 `sessionStorage` 保留五個欄位（使用者實測回來要全部重填）。

★★ 最容易做錯的：① 不要改 `docs/api/*.yaml`、`docs/05`（Leader 複製來的，稽核第 ⑦ 項會擋）；② 不要在前端補 `shippingPolicy` 預設值（ADR-030 否決），
`cartRules.ts` 的判斷不動；③ 前台 dev server 跑著、會 hot reload——不要停它、不要跑 `next build`；④ 草稿用 `sessionStorage` 不用 `localStorage`，只存五個欄位、送出成功就刪；
⑤ `types.admin.ts` 預期零 diff。

package: FE-26
doc: docs/29-前端第十五波派工書.md
allow: frontend/packages/api-client/src/types.storefront.ts
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/apps/storefront/app/(checkout)/checkout/page.tsx
allow: frontend/apps/storefront/app/(checkout)/_lib/
allow: frontend/apps/storefront/app/(account)/me/
allow: frontend/apps/storefront/app/_lib/tabs.ts
allow: frontend/apps/storefront/app/_lib/__tests__/tabs.test.ts

> `types.admin.ts` 在 allow 裡只是因為 codegen 會一起重寫它——預期零 diff，有 diff 就停下來回報。

---

<!--
★ 2026-09-02 晚已通過整合驗收並提交（前端 668e0b7），撤包。原文保留供追溯。

「我的」有家了：`/me`（getMe、三個入口、登出）、分頁與首頁頭像改指 `/me`、`?next=` 回跳、付款結果頁有限次重查。
測試 317 → 376 條，typecheck／build EXIT=0（build 由 Leader 停掉前台 dev server 後跑，事前問過使用者）。

整合者真瀏覽器複驗（只按畫面移動，密碼由使用者自己輸入）：未登入點「我的」→ `/login?next=%2Fme` →
登入 → 回到 `/me`（名字、遮罩手機、三入口、登出）→ 訂單／地址／儲值金都走得到也回得來 → 登出 → 首頁 →
再點「我的」是登入頁；未登入從商品頁走到結帳送出 → `/login?next=%2Fcheckout`（徽章 2 件還在）→ 登入 →
回到 `/checkout`、購物車 2 件都在；付款結果頁「正在確認付款」5 次後切成「尚未確認付款」＋「重新查詢」。

★ 走旅程時撞到兩個不在這包範圍的真缺陷（記進「現在卡在哪」）：#36 登出不清 `gg_cart` cookie、訪客整個買不了；
#37 純現貨／純預購購物車結帳 500（`shippingPolicy` 契約必填 vs 前端只在混合時問）。

子代理四個自主判斷（多匯出 registerHref／currentNext、`loginHrefForCurrentPage()` 讀 window.location、
登入頁亮「我的」在結帳流程裡不準、「重新查詢」重跑整個排程）全部接受。

## 生效中：FE-25　「我的」總覽頁 ＋ 登入後回到原頁 ＋ 付款結果頁自動重查

使用者 2026-09-02 拍板：派，併入兩個追蹤項。Leader 查證後兩個追蹤項都**不用改碼**
（`GET /v1/cart` 對沒有購物車的訪客只回空車、不寫 DB；`gg_cart` cookie 是 HttpOnly，前端讀不到），
寫進報告即可。真正要做的是「我的」一直沒有家：分頁與首頁頭像都指到 `/orders`、`/wallet` 零入口、
**全站沒有登出**；登入後一律被丟到 `/orders`，結帳送出撞 401 只顯示錯誤、沒有去登入的路。

★★ 最容易做錯的：① `?next=` 只收站內絕對路徑（`//`、scheme、`\` 一律回 `/me`），用純函式＋測試釘住；
② 付款結果頁的重查是**有限次**，不准無限輪詢；③ `tabs.test.ts` 那兩段釘「我的 → /orders」的斷言允許改，
但總條數只能增不能減；④ 不要跑 `next build`（dev server 在跑，共用 `.next`）；⑤ 不要動 `useCartItemCount.ts`。

package: FE-25
doc: docs/28-前端第十四波派工書.md
allow: frontend/apps/storefront/app/(account)/
allow: frontend/apps/storefront/app/(checkout)/checkout/page.tsx
allow: frontend/apps/storefront/app/(checkout)/payment/
allow: frontend/apps/storefront/app/(checkout)/_lib/
allow: frontend/apps/storefront/app/(shop)/_components/HomeSearchHeader.tsx
allow: frontend/apps/storefront/app/_lib/

> `app/_lib/` 給整個目錄，**但只准動 `tabs.ts`、新檔 `auth.ts` 與 `__tests__/`**；`(checkout)/_lib/` 只准加新檔與它的測試。
> `checkout/page.tsx` 只准動送出撞 401 那一段；`payment/[orderId]/page.tsx` 只准動 401 導向；`payment/result/page.tsx` 只准加重查與按鈕。
-->

---

<!--
★ 2026-09-02 已通過整合驗收並提交（前端 41e9fd7），撤包。原文保留供追溯。

#32 已解：商品詳情、購物車、結帳三頁各有一條 sticky 頂部列（左返回、中頁名、
商品頁右邊帶徽章的購物車），提示加「查看購物車」，#31 一併修掉。
測試 248 → 317 條，typecheck／build EXIT=0（build 由 Leader 停掉 dev server 後跑，跑完重啟）。

整合者真瀏覽器複驗（不採信自述）：商品頁頂部列 sticky top 0、高 56px、返回指向 /、
徽章 8 件與購物車一致、沒有分頁列；加入購物車後徽章 8→9 就地更新、POST /v1/cart/lines 200、
提示裡有「查看購物車」連到 /cart；/cart 標題「購物車」返回 /、/checkout 標題「結帳」返回 /cart，
兩頁都沒有分頁列；新分頁直接打商品網址（referrer 空）按返回回到 /；body padding-bottom 仍 72px；
開團列表與詳情頁 console 零 hydration 訊息（先塞一個標記訊息當正向對照，確認 console 抓得到）。

★ 子代理五個自主判斷全對：① 第四顆圖示與既有 IconUser 逐字相同，不重複搬；
② 徽章取數抽成共用 hook，分頁列淨減 30 行、畫面不變；③ 購物車／結帳四個狀態分支都包進頂部列，
失敗那一頁也有出口；④ 頂部列用白名單，靠「每一條路由恰好有一種殼」的原始碼掃描測試當保證；
⑤ 新測試第一次跑就抓到它自己寫的順序 bug（/payment/result 被 /payment/:orderId 吃掉），當場修正。
Leader 這次有照「派工前先 commit 閘門檔」做（aeea661），子代理收工沒有再撞 stop gate。

## 生效中：FE-24　三頁的頂部列（商品詳情 · 購物車 · 結帳）＋ #31

修「現在卡在哪」**#32**。FE-23 撤包後使用者接著測，問「昨天提到的加入購物車後
沒有按鈕可以返回首頁，這個沒優化嗎？」——答案是**沒有**：FE-23 把分頁列放上了
首頁、列表、我的訂單，但使用者撞到的那三頁（商品詳情、購物車、結帳）正好是
分頁列刻意隱藏的三頁，一頁都沒修到。商品詳情頁 HTML 裡**零**內部連結（預購商品
才有一條「查看開團」），購物車與結帳有東西時只有「前往結帳」／「送出訂單」。

★ 這是 Leader 的錯：FE-23 派工書只寫「這三頁不要顯示分頁列」，沒有給替代出口。
手機電商商品頁不放分頁列的同時一定有一條頂部列（返回＋購物車）。
驗收時 Leader 用工具直接跳網址，不需要在畫面上找路，所以又沒抓到——跟 #30 同一個形狀。

★★ 這一包最容易做錯的：① 「每一條路由恰好有一種殼」要用原始碼掃描的測試釘住
（照 `bottomActionBarCollision.test.ts` 的做法），不要再抄一份清單；② 返回在直接打網址
進來時也要有地方去（商品→`/`、購物車→`/`、結帳→`/cart`）；③ 不要動 `globals.css:88` 那行
body 留白（上一包的 144px 教訓）；④ 不要跑 `next build`（dev server 在跑，共用 `.next`）。

★ `Toast` 後台也在用，新 prop 必須選填。`packages/ui` 沒有 vitest，測試寫在 `apps/storefront`。
基準 **248 條**，交付時必須變多。

package: FE-24
doc: docs/27-前端第十三波派工書.md
allow: frontend/apps/storefront/app/layout.tsx
allow: frontend/apps/storefront/app/_components/
allow: frontend/apps/storefront/app/_lib/
allow: frontend/apps/storefront/app/(shop)/products/[productId]/page.tsx
allow: frontend/apps/storefront/app/(shop)/products/[productId]/_components/AddToCartPanel.tsx
allow: frontend/apps/storefront/app/(shop)/campaigns/[campaignId]/_components/CampaignOfferRow.tsx
allow: frontend/apps/storefront/app/(checkout)/cart/page.tsx
allow: frontend/apps/storefront/app/(checkout)/checkout/page.tsx
allow: frontend/packages/ui/src/components/
allow: frontend/packages/ui/src/tokens/soft-seoul.css
allow: frontend/packages/ui/src/index.ts

> `packages/ui/src/components/` 給整個目錄是因為 `TopBar.tsx`（新檔）還不存在；
> **只准動 `packages/ui/src/components/Toast.tsx`、`icons/index.tsx`、`Countdown.tsx` 與新檔 `TopBar.tsx`**，其餘動了會被退回。
> `apps/storefront/app/layout.tsx` 只在你選 layout 層機制時才動。三頁各自的頁面檔案只加頂部列，不改版面與邏輯。
-->

---

<!--
★ 2026-09-02 已通過整合驗收並提交（前端 5b2db68），撤包。原文保留供追溯。

#30 已解：前台四個分頁（首頁 · 開團 · 購物車 · 我的）＋購物車數量徽章。
測試 150 → 248 條。整合者複驗：11 條路由的顯示與否全部符合預期、真瀏覽器
實測 body padding-bottom 為 72px（沒有加倍）、徽章 5 件與購物車實際內容（1+4）
相符、加入後徽章 7→8 就地更新且未重新整理。

★★ 這一包最值得記住的是**「派工書寫錯、子代理擋下來」第四次發生**：
Leader 在 §1 要求「在 layout 包裹層加底部留白」，但 globals.css:88 早就在
<body> 上放了同一個算式——照字面做會變成 144px。子代理去抓 dev server 真的
送出去的 CSS 查證後否決，保留目的、換位置達成，並用測試去讀 globals.css
逐字比對釘住。Leader 在真瀏覽器複驗確認它是對的。

★ 兩個由「可及名稱會不會講出不成立的事」決定的判斷：
① 徽章取數量總和而非品項數——一行 quantity=5 唸成「1 件」是錯的，而且後端會
   把同一個 SKU 併行，取 lines.length 時「再加一次」徽章不會動＝按了沒反應。
② 拿不到購物車時不畫徽章、不退化成 0，但「空車」與「不知道」在可及名稱上仍
   分得出來。跟 #29、FE-21 同一條紅線。

★ 用黑名單而非白名單：白名單會讓下一個新頁面預設沒有導覽，那正是 #30 換一頁
再發作一次。代價由 bottomActionBarCollision.test.ts 機械擋著。

★ Leader 的流程疏失：沒有照 PROMPTS.md 第 316 行「派工前先 commit 閘門檔」做，
結果子代理每一輪收工都被 stop gate 要求還原 .dispatch/ACTIVE.md（＝它自己的
授權）。它兩次都正確拒絕並舉證（開工前就是 M、mtime 早它五分鐘、diff 內容就是
package: FE-23）。下次一定要先 commit。

## 生效中：FE-23　前台的殼（底部分頁列）

修「現在卡在哪」**#30**。使用者 2026-09-02 從前台測整段下單時撞到：
加完購物車之後**沒有任何按鈕回得去**，只能按上一頁；
而且**首頁上連「購物車」三個字都沒有**，`/cart` 只能自己打網址進去。

根因不是誰漏做，是當初就沒排：`docs/06-前端工作包.md` 八包裡
**只有 FE-6「後台：殼、登入與營運儀表板」有殼這一包，前台從來沒有**。
FE-3／4／5 做的是前台的頁面，沒有人做框。八包做完、五波驗收都沒抓到，
因為**只有在真瀏覽器裡連續操作才會發現**——單獨測每一頁時每一頁都是好的。

★★ 這一包最容易做錯的地方：`BottomActionBar` 是 `fixed bottom-0`、高 72px，
**已經有三頁在用**（商品詳情的 `AddToCartPanel`、購物車頁、結帳頁），
兩條固定列疊在一起會蓋住內容。那三頁不要顯示分頁列，而且要有測試釘住這份清單。

★ 徽章不准在拿不到資料時顯示 0——「0 件」與「不知道幾件」是兩件事。
這個專案一再踩到的失敗形狀就是「畫面宣稱了不成立的事」（#29、FE-21）。

★ 沿用 FE-21／FE-22 兩個既有結論：`pnpm lint` 在這個 workspace 跑不起來（沒裝 ESLint），
不要列進自驗；測試要放在 `apps/storefront`（有 vitest），寫在 `packages/ui` 的
永遠不會被執行。基準 **150 條**，交付時必須變多。

package: FE-23
doc: docs/26-前端第十二波派工書.md
allow: frontend/apps/storefront/app/layout.tsx
allow: frontend/apps/storefront/app/_components/
allow: frontend/apps/storefront/app/_lib/
allow: frontend/apps/storefront/app/(shop)/products/[productId]/_components/AddToCartPanel.tsx
allow: frontend/apps/storefront/app/(shop)/campaigns/[campaignId]/_components/CampaignOfferRow.tsx
allow: frontend/apps/storefront/app/(checkout)/cart/page.tsx

> 後兩個呼叫點與購物車頁列進來，是因為徽章要能立即更新（加入時 ＋1、移除時 −1）。
> **只改通知徽章那幾行，不要改它們的版面或邏輯。**
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（前端 6f3f380），撤包。原文保留供追溯。

#27／#28 都已解，Leader 用當初查出這兩個 bug 的同一段瀏覽器探針複驗：
點愛心後網址仍是 `/products`、`aria-pressed` 由 false 變 true（真的切換而不是導航）；
login 兩個、register 三個標 `*` 的欄位都是 `required: true`，兩個選填的維持 false。

★★ 這一包最值得記住的是**「休眠測試」這個形狀**：
子代理第一輪把測試寫在 `packages/ui/src/components/__tests__/`，
但 `packages/ui` **既沒有 `test` script 也沒有 vitest 相依**，
`pnpm --recursive test` 根本跑不到它——**146 條前後完全沒變**。
它正確地回報了這件事，也沒有自己去動 `package.json`／`pnpm-lock.yaml`。
**Leader 的裁決是搬家而不是加相依**：`suppressCardNavigation` 已從 `packages/ui`
公開入口 export，而 `apps/storefront` 依賴 `@greygray/ui` 且有 vitest。
搬完 **146 → 150**，而且**先紅後綠在新位置重做**：拿掉 `preventDefault` 之後
`pnpm --recursive test` 回 **EXIT=1**——搬家前做同樣的破壞，那個指令還是全綠 EXIT=0。
**那一行 EXIT=1 才是搬家買到的東西。**
「迴歸保證如果永遠不會跑，就不是保證」，跟 #29 是同一個形狀。

★ 子代理還多做了一步：拿掉 `@ts-expect-error` 後 typecheck 沒紅，
但它指出「沒紅」不等於「型別在保護測試」——如果 `expect` 仍是 `any` 也不會紅。
所以故意把 `toHaveBeenCalledTimes(1)` 傳字串，拿到 `TS2345` 才算數。探針已還原。

★ 追蹤項：`register/page.tsx:67` 的文案與 `CLAUDE.md:219`（ADR-019）牴觸
（那裡明文說註冊頁不要寫「舊訂單請到原平台查詢」）。子代理查到但沒改——
改文案是產品決定。Leader 裁決維持現況。

詳見 `.dispatch/reports/FE-22.md` 與 `GreyGray_PM/03-驗收紀錄.md` 第二十四次。

派工 FE-22：收藏心的導航攔截與前台必填欄位（#27／#28）　·　docs/25-前端第十一波派工書.md

2026-09-01 真 Chrome 全站逐頁複驗查出的兩個前端小缺陷，一起做掉。

★ Leader 已查證並裁決：
① **#27 根因是 `stopPropagation()` 擋不住 `<a>` 的預設導航**——
   `ProductCardLink.tsx:39` 用 `<Link>` 包住整張卡，而 `ProductCard.tsx:97` 的收藏心
   只包了 `onClick={(e) => e.stopPropagation()}`。要擋導航必須 `preventDefault()`。
   **不要把 ProductCard 搬出 `<Link>`、不要改 ProductCardLink 的結構**——
   那會動到整個列表的導航行為，風險遠大於這個 bug。
② **#28 根因是 `Field` 的 `required` 只負責畫星號、不會傳給 input**——
   後台 `apps/admin/app/login/page.tsx` 是正確寫法（`<Field required>` **而且**
   `<Input required>`，第 80／85、91／96 行），前台只寫了前者。照後台補上即可。
   **只補標了 `*` 的欄位**，`register-email` 與 `register-referral-code` 畫面上寫著
   「選填」，不要給它們 `required`。
③ **這個 workspace 沒有 jsdom 也沒有 `@testing-library`，而且沒有安裝**（Leader 已查證）。
   **不要為了寫測試去加相依套件、不要動 `pnpm-lock.yaml`。**
   #27 的迴歸保證改用「把 handler 抽成可單元測試的小函式」，用假 event 斷言
   `preventDefault` 與 `stopPropagation` 兩者都被呼叫。
④ **不改表單的送出行為**：目前兩個表單都是「空白也送出、由伺服器回 401 並顯示訊息」，
   那是既有行為（後台那個表單甚至刻意 `noValidate`）。這一包只補無障礙屬性。
   若補上 `required` 之後瀏覽器開始擋送出、使既有錯誤訊息路徑走不到，**停下來問**。
⑤ ★ `pnpm lint` 在這個 workspace **根本跑不起來**（沒裝 ESLint，`next lint` 已棄用且
   互動式；對沒碰過的 storefront 跑也是 exit 1，Leader 已用對照組確認）。
   **不要把它列進自驗、也不要假裝通過**——FE-21 的子代理正確地回報「做不到」。

package: FE-22
doc: docs/25-前端第十一波派工書.md
allow: frontend/packages/ui/src/components/ProductCard.tsx
allow: frontend/packages/ui/src/components/__tests__/
allow: frontend/apps/storefront/app/(account)/login/page.tsx
allow: frontend/apps/storefront/app/(account)/register/page.tsx
allow: frontend/apps/storefront/app/(shop)/_components/__tests__/
-->

★ 2026-09-01 補列（第二輪）：子代理正確回報「新測試是休眠的」——
`packages/ui` 既沒有 `test` script 也沒有 vitest 相依，`pnpm --recursive test`
根本跑不到它（146 條前後完全沒變）。它沒有自己去動 `package.json`／`pnpm-lock.yaml`，
處理正確。**Leader 的裁決是搬家而不是加相依**：`suppressCardNavigation` 已經從
`packages/ui` 的公開入口 export（`index.ts:36` 的 `export * from './components/ProductCard'`），
而 `apps/storefront` 依賴 `@greygray/ui`（`workspace:*`）**且有 vitest**——
把測試搬到 storefront 就會真的跑，**零相依變更、lockfile 零改動**，
順帶還能拿掉那個為了 `TS2307` 加的 `@ts-expect-error`（型別會回來，不再是 `any`）。

---

<!--
★ 2026-09-01 已通過整合驗收並提交（前端 ebe074c），撤包。原文保留供追溯。

#29 解掉：後台首頁的財務數字與最近分錄不再是寫死的假資料。
Leader 真瀏覽器複驗：首頁顯示 NT$0 與 -NT$1,920，與同一時刻 API 回傳的
`customerLiability=0`／`cash=-192000` **逐字相符**；分錄表格 2 列 ＝ API 1 筆分錄的
借貸兩行；舊假數字 1,280,000／860,000 在畫面上零命中；KPI 四卡維持「尚未提供」。
測試 135 → **146 條**全過（admin 60→71）。

★★ 這一包最值得記住的兩件事：
① **「絕對不准 fallback 回寫死的數字」是用型別保證的，不是靠自律**——
   新的 `LoadState<T>` 只有三態，`ready` 以外的分支在型別上就拿不到 `data`。
   跟 BE-35 修 #22 時同一個原則：讓錯的寫法編不過，而不是「記得別寫」。
② **11 條測試才是這一包的主產出**。#29 能活到現在，正是因為**沒有任何測試
   斷言過「首頁顯示的數字來自 API」**。最關鍵那條是「整頁首次 render
   （還沒有任何 API 回應）不准出現任何金額」——先紅後綠實跑，
   HEAD 版的頁面在連一支 API 都還沒打時就已經畫出 13 個金額。

★ 子代理另外主動修了兩處同型的病（都申報了，Leader 接受）：排序表頭本來是死的
（`handleSortChange` 存在但沒接到 `DataTable`）；「重新整理」按鈕本來只跳一個
success toast 卻不重新取數——在請求還沒送出時就宣告成功。都是「畫面宣稱了
不成立的事」，與 #29 同一種病。

★ 子代理正確地拒絕假裝通過一條做不到的自驗（`pnpm lint`），見上面 FE-22 的裁決⑤。
詳見 `.dispatch/reports/FE-21.md` 與 `GreyGray_PM/03-驗收紀錄.md` 第二十三次。

派工 FE-21：後台首頁接上真的帳務端點（#29）　·　docs/24-前端第十波派工書.md

2026-09-01 使用者登入後台後，Leader 用真 Chrome 逐頁測，查出**後台首頁的財務數字與
最近分錄是寫死的假資料**：首頁顯示「客戶負債 NT$1,280,000／現金 NT$860,000、
資料時間 2026年8月28日」並跳紅字警示「正在用還沒交貨的錢過日子」，
但同一份資料在帳務頁與 API 是「客戶負債 NT$0／現金 -NT$1,920、即時」；
首頁「最近分錄」列 6 筆，而 `GET /v1/ledger/entries` **只有 1 筆**。
**兩頁互相矛盾，而老闆會看首頁做經營判斷。**

★ Leader 已查證並裁決：
① 根因是 `(dash)/_lib/dashboardMock.ts` 的兩個 fixture 被 `(dash)/page.tsx` 直接使用。
   **那個檔案自己的註解就寫著要換成 `GET /v1/ledger/liability-vs-cash` 與
   `GET /v1/ledger/entries`**，而這兩個端點現在都存在也都正常，
   `packages/api-client` 的 `getLiabilityVsCash`／`listLedgerEntries` 也早就有。
② **不要重新設計**：`(dash)/ledger/page.tsx` 已經正確地做完這件事
   （Leader 在真瀏覽器裡確認它顯示真資料），照抄那個形狀。
③ **四個 KPI 卡維持「尚未提供」**——那是誠實的空值，不是假數字；
   更不准用列表 API 在前端加總（列表有分頁，加出來只是這一頁的合計）。
④ 必須補測試釘住「首頁顯示的數字來自 API」。**#29 能活到現在，
   正是因為沒有任何測試斷言過這件事**；沒有那條測試，改完還會再退化。
⑤ 載入中與失敗時**絕對不准 fallback 回任何寫死的數字**——那正是這個 bug 的成因。

package: FE-21
doc: docs/24-前端第十波派工書.md
allow: frontend/apps/admin/app/(dash)/page.tsx
allow: frontend/apps/admin/app/(dash)/_lib/
allow: frontend/apps/admin/app/(dash)/_components/LiabilityVsCashCard.tsx
allow: frontend/apps/admin/app/(dash)/__tests__/
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 31ce3b1），撤包。原文保留供追溯。
跟進後端第十五波（BE-31，ADR-026）已凍結進契約的 quantityShortfall 欄位與
refund-shortfall 端點。子代理過程中撞到 ai-cli MCP 連線中斷，Leader 改用
ListAgents／SendMessage 直接聯繫同一個子代理 session 續完；審查時發現子代理
留了一段除錯用的假資料 fixture 誤寫進 page.tsx 的初始 state（會讓正式頁面
短暫顯示假訂單），已要求子代理自行改回並補完自驗報告，Leader 複驗通過。
即時驗證受限於這個環境沒有瀏覽器自動化工具、(dash) 版面 SSR 恆回傳空殼、
dev DB 目前沒有任何 order 資料，只做到型別／邏輯層級驗證，詳見
.dispatch/reports/FE-20.md 與 GreyGray_PM/03-驗收紀錄.md。

派工 FE-20：訂單品項顯示短缺數量＋新增「退短缺款」操作入口　·　docs/23-前端第九波派工書.md

package: FE-20
doc: docs/23-前端第九波派工書.md
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/endpoints/admin.ts
allow: frontend/apps/admin/app/(dash)/orders/_components/RefundShortfallDialog.tsx
allow: frontend/apps/admin/app/(dash)/orders/[orderId]/page.tsx
allow: frontend/apps/admin/app/(dash)/orders/_lib/labels.ts
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 ebdd36d），撤包。原文保留供追溯。
修法（middleware 讀 x-forwarded-host／host 手動組 origin）即時驗證證實生效，
見 .dispatch/reports/FE-19.md。

派工 FE-19：middleware 改讀反向代理 header 手動組 origin，取代失敗的 trustHostHeader　·　docs/22-前端第八波派工書.md

package: FE-19
doc: docs/22-前端第八波派工書.md
allow: frontend/apps/admin/next.config.ts
allow: frontend/apps/admin/middleware.ts
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 83d2fda），撤包。原文保留供追溯。
診斷性交付：修法對目前部署拓樸沒有效果，見 .dispatch/reports/FE-18.md。
下一波 FE-19（docs/22）改用 middleware 讀反向代理 header 的做法接手。

派工 FE-18：修 admin 正式機導向永遠指向 localhost　·　docs/21-前端第七波派工書.md

package: FE-18
doc: docs/21-前端第七波派工書.md
allow: frontend/apps/admin/next.config.ts
-->

---

<!--
★ 2026-08-30 已執行並提交（前端 d48acbe），撤包。原文保留供追溯。
FE-12 沒有把 §5 全部六條走完——不是前端沒做完，是後端環境缺
Identity:DataProtectionKey／Payment:ECPay:MerchantId 兩項設定、
資料庫是空的且無法建立員工帳號、admin BFF 三組端點永久不回應。
FE-12 正確地只記錄不修改，詳見 .dispatch/reports/FE-12.md
與 GreyGray_PM/03-驗收紀錄.md。

派工 FE-12：關掉 mock、對真後端跑一遍　·　docs/18-前端第五波派工書.md

package: FE-12
doc: docs/18-前端第五波派工書.md
allow: frontend/apps/admin/.env.local
allow: frontend/apps/storefront/.env.local
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（前端 50c314c），撤包。原文保留供追溯。

派工 FE-17：前台運費文案改從契約來　·　docs/20-前端第六波派工書.md

package: FE-17
doc: docs/20-前端第六波派工書.md
allow: frontend/apps/storefront/app/(checkout)/
-->

---

## 已經通過、不再生效的（保留軌跡）

- **FE-25** 「我的」總覽頁（`/me`，含登出）＋「我的」改指 `/me` ＋ `?next=` 回跳 ＋ 付款結果頁有限次重查　·　2026-09-02 通過　·　`668e0b7`　·
  測試 317 → 376 條；Leader 真瀏覽器走完登入／登出／結帳 401 回跳；走旅程時撞到 #36、#37（不在此包），見 `.dispatch/reports/FE-25.md`
- **FE-24** 三頁的頂部列 ＋ 提示加「查看購物車」＋ 修 #31（#32）　·　2026-09-02 通過　·　`41e9fd7`　·
  測試 248 → 317 條，見 `.dispatch/reports/FE-24.md`
- **FE-23** 前台的殼：底部分頁列 ＋ 購物車徽章（#30）　·　2026-09-02 通過　·　`5b2db68`　·
  測試 150 → 248 條，見 `.dispatch/reports/FE-23.md`
- **FE-22** 收藏心的導航攔截與前台必填欄位（#27／#28）　·　2026-09-01 通過　·　`6f3f380`
- **FE-21** 後台首頁接上真的帳務端點（#29）　·　2026-09-01 通過　·　`ebe074c`　·　測試 135 → 146 條
- **FE-20** 訂單品項顯示短缺數量＋新增「退短缺款」操作入口　·　2026-08-31 通過　·　`31ce3b1`　·
  跟進 BE-31（ADR-026）契約；即時驗證受限於環境（無瀏覽器自動化、dev DB 無 order 資料），
  只做到型別／邏輯層級驗證，見 `.dispatch/reports/FE-20.md`
- **FE-19** middleware 改讀反向代理 header 手動組 origin，取代失敗的 trustHostHeader　·　2026-08-31 通過　·　`ebdd36d`　·
  修法生效，接手 FE-18 沒解決的部分，見 `.dispatch/reports/FE-19.md`
- **FE-18** 修 admin 正式機導向永遠指向 localhost（診斷性交付）　·　2026-08-31 通過　·　`83d2fda`　·
  修法（`experimental.trustHostHeader`）對目前部署拓樸沒有效果，見 `.dispatch/reports/FE-18.md`；
  下一波 FE-19 改用 middleware 讀反向代理 header 接手
- **FE-12** 關掉 mock、對真後端跑一遍　·　2026-08-30 執行　·　`d48acbe`　·
  §5 部分完成（mock 關閉、build/test/typecheck 全過），完整流程被後端環境缺口擋住，見自驗報告
- **FE-17** 前台運費文案改從契約來　·　2026-08-30 通過　·　`50c314c`

FE-1～FE-8（前兩波 ＋ 技術債收尾 `631e7bc`）· FE-9／FE-10／FE-11（第三波 `bbce5e1`）·
第四波 FE-13／FE-14／FE-15（`e8156e6`）·
第五波 FE-16（`6165419`）
