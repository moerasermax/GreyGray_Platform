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

## 生效中：FE-48 後台登入狀態還原與手機導覽

package: FE-48
doc: docs/50-後台登入還原與手機導覽派工書.md
allow: frontend/apps/admin/app/(dash)/layout.tsx
allow: frontend/packages/ui/src/admin/AppShell.tsx
allow: .dispatch/reports/FE-48.md

---

## 生效中：FE-49 後台依角色隱藏無權限區塊與訂單詳情數字對齊

package: FE-49
doc: docs/51-後台角色感知與數字對齊派工書.md
allow: frontend/apps/admin/app/(dash)/page.tsx
allow: frontend/apps/admin/app/(dash)/orders/[orderId]/page.tsx
allow: .dispatch/reports/FE-49.md

---

## 生效中：FE-37　定稿七日鑑賞期 ＋ 隱私權政策

使用者 2026-09-21 明確決定：本站所有商品適用七日鑑賞期，並立即新增隱私權政策。只改五個資訊頁檔案；不得捏造法定公司資料、聯絡資料、保存期限或安全承諾。

package: FE-37
doc: docs/39-前端第二十三波派工書.md
allow: frontend/apps/storefront/app/(info)/_content/terms.ts
allow: frontend/apps/storefront/app/(info)/_content/privacy.ts
allow: frontend/apps/storefront/app/(info)/privacy/page.tsx
allow: frontend/apps/storefront/app/(info)/_components/InfoLinks.tsx
allow: frontend/apps/storefront/app/(info)/__tests__/infoPages.test.ts
allow: .dispatch/reports/FE-37.md

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

<!--
★ 2026-09-24 撤包：FE-46／47 已由 Leader 以 Playwright 真畫面驗收並整合提交 985e4e4。原文保留供追溯。

## 已撤包：FE-46 結帳頁版面與付款錯誤出口

(撤包) package: FE-46
(撤包) doc: docs/48-結帳頁版面與付款出口派工書.md
(撤包) allow: frontend/apps/storefront/app/(checkout)/checkout/page.tsx
(撤包) allow: frontend/apps/storefront/app/(checkout)/_components/CheckoutOrderSummary.tsx
(撤包) allow: frontend/apps/storefront/app/(checkout)/payment/[orderId]/page.tsx
(撤包) allow: .dispatch/reports/FE-46.md

---

## 已撤包：FE-47 會員區寬度一致與首頁桌面標題

(撤包) package: FE-47
(撤包) doc: docs/49-會員區寬度一致與首頁標題派工書.md
(撤包) allow: frontend/apps/storefront/app/(account)/me/page.tsx
(撤包) allow: frontend/apps/storefront/app/(account)/wallet/page.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/page.tsx
(撤包) allow: .dispatch/reports/FE-47.md

---

-->

<!--
★ 2026-09-24 撤包：FE-42／44／45 已由 Leader 以 Playwright 真畫面驗收並整合提交 9870cd2。原文保留供追溯。

## 已撤包：FE-44 全站桌面頁首

(撤包) package: FE-44
(撤包) doc: docs/46-全站桌面頁首派工書.md
(撤包) allow: frontend/apps/storefront/app/_components/SiteHeader.tsx
(撤包) allow: frontend/apps/storefront/app/layout.tsx
(撤包) allow: frontend/apps/storefront/app/_components/StorefrontTabBar.tsx
(撤包) allow: frontend/apps/storefront/app/_components/PageTopBar.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/_components/HomeSearchHeader.tsx
(撤包) allow: .dispatch/reports/FE-44.md

---

## 已撤包：FE-45 服務條款與登入頁精修

(撤包) package: FE-45
(撤包) doc: docs/47-條款與登入頁精修派工書.md
(撤包) allow: frontend/apps/storefront/app/(info)/terms/page.tsx
(撤包) allow: frontend/apps/storefront/app/(account)/login/page.tsx
(撤包) allow: .dispatch/reports/FE-45.md

---

## 已撤包：FE-42 會員頁視覺精修

(撤包) package: FE-42
(撤包) doc: docs/44-會員頁視覺精修派工書.md
(撤包) allow: frontend/apps/storefront/app/(account)/register/page.tsx
(撤包) allow: frontend/apps/storefront/app/(account)/me/page.tsx
(撤包) allow: frontend/apps/storefront/app/(account)/orders/page.tsx
(撤包) allow: .dispatch/reports/FE-42.md

---

-->

<!--
★ 2026-09-24 撤包：FE-38／39／40／41／43 已由 Leader 重跑 typecheck 與 584 測試並整合提交 34f253b。原文保留供追溯。

## 已撤包：FE-43 資訊頁閱讀精修

(撤包) package: FE-43
(撤包) doc: docs/FE-43-資訊頁閱讀精修派工書.md
(撤包) allow: frontend/apps/storefront/app/(info)/about/page.tsx
(撤包) allow: frontend/apps/storefront/app/(info)/guide/page.tsx
(撤包) allow: frontend/apps/storefront/app/(info)/faq/page.tsx
(撤包) allow: .dispatch/reports/FE-43.md

---

## 已撤包：FE-41 開團頁視覺精修（待自測與提交後派出）

(撤包) package: FE-41
(撤包) doc: docs/43-開團頁視覺精修派工書.md
(撤包) allow: frontend/apps/storefront/app/(shop)/_components/CampaignCard.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/campaigns/[campaignId]/page.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/campaigns/[campaignId]/_components/CampaignOfferRow.tsx
(撤包) allow: .dispatch/reports/FE-41.md

---

## 已撤包：FE-40 商品詳情與購物車視覺精修

(撤包) package: FE-40
(撤包) doc: docs/42-商品詳情與購物車視覺精修派工書.md
(撤包) allow: frontend/apps/storefront/app/(shop)/products/[productId]/page.tsx
(撤包) allow: frontend/apps/storefront/app/(checkout)/cart/page.tsx
(撤包) allow: frontend/apps/storefront/app/(checkout)/_components/CartLineRow.tsx
(撤包) allow: frontend/packages/ui/src/components/BottomActionBar.tsx
(撤包) allow: .dispatch/reports/FE-40.md

---

## 已撤包：FE-39 商品卡與導覽視覺精修

(撤包) package: FE-39
(撤包) doc: docs/41-商品卡與導覽視覺精修派工書.md
(撤包) allow: frontend/packages/ui/src/components/ProductCard.tsx
(撤包) allow: frontend/packages/ui/src/components/CategoryChip.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/_components/ProductCardLink.tsx
(撤包) allow: frontend/apps/storefront/app/_components/StorefrontTabBar.tsx
(撤包) allow: .dispatch/reports/FE-39.md

---

## 已撤包：FE-38　前台視覺精修（待閘門自測後派出）

(撤包) package: FE-38
(撤包) doc: docs/40-前台視覺精修派工書.md
(撤包) allow: frontend/packages/ui/src/tokens/soft-seoul.css
(撤包) allow: frontend/apps/storefront/app/(shop)/_components/HeroBanner.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/_components/HomeSearchHeader.tsx
(撤包) allow: frontend/apps/storefront/app/(shop)/page.tsx
(撤包) allow: .dispatch/reports/FE-38.md

---

-->

<!--
★ 2026-09-19 三包都已通過整合驗收並提交，撤包。原文保留供追溯。

FE-34（sonnet＋medium）：`8fb5f34` ＋ Leader 驗收後補做 `06647c6`。補做兩件事——
① 舊訂單（ADR-039 之前）的收件人那一行原本會整段消失，新增 `recipientDisplayOf()`，
快照為 null 時 fallback 回 `shippingAddress` 並在畫面標示；
② 後台新增「收件地址」欄位（BE-54 挖出後台完全沒有地址欄位、宅配一樣寄不出去）。
它自己順手修掉 Leader 契約裡 `Problem` → `ProblemDetails` 的 typo（會讓 codegen 整個跑不動）。

FE-35（sonnet＋medium）：`04a1510`。自驗時自己抓到並修掉一個 reducer bug
（`markResolved` 反而把視窗重開）。Leader 用真瀏覽器走完整流程到資料庫，
工單的 `menu_path` 正確記著 `["其他問題"]`。

FE-36（sonnet＋medium）：`e21930d` ＋ Leader 走查後補做 `68d98a2`。
補做的是走查當場抓到的兩包交互作用缺陷：小幫手重用 `FAQ_GROUPS`，
而 FAQ 答案寫著「請點右下角的客服小幫手」——人就在小幫手裡面。
位置指引已集中到 `faq/page.tsx` 頁面層級，不放進 `FAQ_GROUPS`。

Leader 驗：`pnpm -r test` 654 → 768（storefront 552／admin 169／api-client 47）、
`pnpm -r typecheck` 全綠、兩個 app 的 `build` 代跑通過。

⚠ 留下：真正的 360px viewport 走查這個環境做不到（Chrome 桌面視窗最小 516px、
擴充沒開放裝置模擬）；需要登入才能走的走查（愛心、立即購買、選門市回填、
後台客服訊息頁）尚未做。

## 生效中（已撤包）：FE-34　結帳頁收件人姓名與手機 ＋ 前後台訂單詳情顯示明文（ADR-039）

使用者 2026-09-19 `/goal` 拍板：收件人姓名手機要加，**後台全員看明文、不遮罩、不加解鎖按鈕**。
計畫書在後端樹 `docs/53-第四十波計畫書.md`（`8ab47fc`）。契約 Leader 已凍結，**照派工書做、不要等 BE-54 交付**。

★★ 最容易做錯的：① **草稿還原**——選門市會跳出站外再回來，新欄位一定要進 `currentDraft()` 與 `loadCheckoutDraft` 的還原流程，
漏了客人回來欄位就空了，**要有測試**；② **宅配模式不顯示也不送**這兩個欄位（後端從地址簿抄）；
③ 手機要檢查 `09` 開頭十碼（比既有的 `validateAddressForm` 嚴，因為超商會擋）；
④ 後台**拿掉 `MaskedContactNote.tsx`**、不要做「點一下看明文」的按鈕；
⑤ `app/layout.tsx`、`app/_components/`、`packages/ui/` 是 FE-35 的，`app/(info)/` 是 FE-36 的。

package: FE-34
doc: docs/36-前端第二十二波派工書-第一包.md
allow: frontend/apps/storefront/app/(checkout)/
allow: frontend/apps/storefront/app/(account)/orders/
allow: frontend/apps/admin/app/(dash)/orders/
allow: frontend/packages/api-client/
allow: .dispatch/reports/FE-34.md

---

## 生效中（已撤包）：FE-35　右下角客服小幫手（引導式選單、不接 AI）＋ 後台「客服訊息」頁（ADR-040）

FAQ 有三處寫「請聯絡客服」但站上沒有客服管道。右下角展開式小視窗，選項與答案**重用 `FAQ_GROUPS`**，
選不到答案才留言；留言進後台工單列表。契約 Leader 已凍結，不要等 BE-55。

★★ 最容易做錯的：① **不接 AI、不接第三方客服套件**（ADR-040 明確否決）；
② 選項文案**重用 `app/(info)/_content/faq.ts` 的 `FAQ_GROUPS`**，不要另抄一份（FE-36 正在改那些字，抄了會走鐘）；
③ **不能擋住結帳頁的送出鈕與購物車的結帳鈕**，360px 下尤其要檢查，關閉鈕一定要看得見、不要疊在分頁列上；
④ **不要改 `packages/api-client/`**（FE-34 正在動），客服的型別與呼叫寫在自己的 `_lib` 裡；
⑤ `app/(info)/` 只能 import 不能改；⑥ 前台**不做**「查詢我的工單」頁（匿名工單沒有安全的查詢方式）。

package: FE-35
doc: docs/37-前端第二十二波派工書-第二包.md
allow: frontend/apps/storefront/app/_components/
allow: frontend/apps/storefront/app/_lib/
allow: frontend/apps/storefront/app/layout.tsx
allow: frontend/apps/admin/app/(dash)/layout.tsx
allow: frontend/apps/admin/app/(dash)/tickets/
allow: frontend/packages/ui/
allow: .dispatch/reports/FE-35.md

---

## 生效中（已撤包）：FE-36　服務條款頁 ＋ FAQ 的鑑賞期與客服文案

使用者 2026-09-19 `/goal` 拍板：**鑑賞期寫在服務條款**。站上沒有服務條款頁，要新建。
`faq.ts` 裡那行「拿掉七天鑑賞期（ADR-025：法律適用由老闆判斷）」的註解要刪掉——**老闆現在判斷了**。
條款全文在派工書裡，照抄。**這一包完全獨立，可以最先交付。**

★★ 最容易做錯的：① 條款第五、六節有兩個**老闆確認項**，原樣保留成看得見的提醒，**不要自己刪、不要自己判斷法律適用**；
② 三處「請聯絡客服」改成指向右下角小幫手，**不要寫死 LINE／Email／電話**（使用者還沒給，寫了就是假的）；
③ **不要改 `FaqGroup`／`FaqItem` 的型別或匯出名字**——FE-35 要 import `FAQ_GROUPS`；
④ FAQ 只寫指路，法律細節一律放服務條款一處；⑤ 這一頁要有出口，360px 下長文不要橫向捲動。

package: FE-36
doc: docs/38-前端第二十二波派工書-第三包.md
allow: frontend/apps/storefront/app/(info)/
allow: .dispatch/reports/FE-36.md

---
-->
<!--
★ 2026-09-15 已通過整合驗收並提交（前端 `ff132af`），撤包。原文保留供追溯。

FE-33（Claude opus＋medium）一輪：api-client 重生（admin 只多三欄）＋ 兩支端點（body 型別從 `paths` 推導）＋ mock／既有超商 fixture 補欄位；`(checkout)/_lib/cvsSelection.ts`（回程四選一、進頁合併、只清選店票、錯誤訊息表、同步鎖與 `pageshow` 放鎖、讀票與送出失敗分支，全部可注入測試）；`ConvenienceStoreField` 改電子地圖；結帳頁 Suspense（fallback 同一組 Skeleton）＋ 固定七步初始化 ＋ 只送 `convenienceStoreSelectionId`；前台訂單詳情加地址、後台加「取貨門市」。測試 558 → **643**。
Leader 驗：`pnpm --recursive typecheck` EXIT=0、643 全過（api-client 46／admin 142／storefront 455）、storefront 與 admin build EXIT=0、audit 通過、diff 範圍在 allow 內、checkout 頁與 `cvsSelection.ts` 原文讀過；與 BE-53 合起來以 HTTP 走完選門市到付款 32 項全過（前台 `/checkout?cvsSelection=…` 與訂單詳情頁 200）。
⚠ 真瀏覽器畫面走查**尚未做**（Chrome 擴充仍未連線）：「選好門市後重新整理門市還在」「按上一頁回來鎖放開」「360px」只有純函式測試與推理，待補。
留下：`createCvsMapSession` 仍會被 `http.ts` 自動帶冪等鍵 header（後端忽略，無害）；mock 模式沒有假地圖可走；沒有草稿但網址帶票時配送方式不自動選超商取貨。

## 生效中（已撤包）：FE-33　結帳頁用 7-ELEVEN 電子地圖選門市 ＋ 前後台訂單詳情顯示門市（ADR-038）

使用者 2026-09-15 以 `/goal` 問「可以新增 7-11 收貨嗎」。現在的 `ConvenienceStoreField.tsx` 是手動輸入門市代號（檔頭自己寫「假設，未接真正的綠界電子地圖」），
訂單詳情只顯示後端塞進來的代號、後台完全沒有門市。契約（三條 `/v1/logistics/*`、`CheckoutRequest.convenienceStoreSelectionId`、`Order.convenienceStoreAddress`、`AdminOrder` 三個門市欄位）
與 ADR-038 Leader 已從後端樹逐位元複製進來；後端 BE-53 同一波平行實作。派工書經 Codex 逐行覆驗 13 條與 Gemini 情境覆驗 10 條。

★★ 最容易做錯的：① 整頁離開去地圖再回來，**進頁初始化照固定順序**（讀草稿 → 解析回程 → 純函式合併 → 一次設 state → 存回完整草稿 → 清網址 → 讀票），不靠 effect 順序；
② 回程錯誤**一律清掉選店票**，404／422 清票**不准刪整份草稿**；③ 同步 `pendingRef` 鎖 ＋ `pageshow` persisted 放鎖；④ 可否送出看「讀到了門市」；
⑤ `useSearchParams` 要 Suspense、fallback 不准 null、`pageShell.test.ts` 不改；⑥ 不准手改生成檔、mock 既有超商訂單 fixture 要補欄位；⑦ `pnpm` 指令在 `frontend/` 裡跑。

package: FE-33
doc: docs/35-前端第二十一波派工書.md
allow: frontend/packages/api-client/src/types.storefront.ts
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/endpoints/storefront.ts
allow: frontend/packages/api-client/src/endpoints/README.md
allow: frontend/packages/api-client/src/mock/
allow: frontend/apps/storefront/app/(checkout)/checkout/
allow: frontend/apps/storefront/app/(checkout)/_components/ConvenienceStoreField.tsx
allow: frontend/apps/storefront/app/(checkout)/_components/__tests__/
allow: frontend/apps/storefront/app/(checkout)/_lib/cvsSelection.ts
allow: frontend/apps/storefront/app/(checkout)/_lib/checkoutDraft.ts
allow: frontend/apps/storefront/app/(checkout)/_lib/cartRules.ts
allow: frontend/apps/storefront/app/(checkout)/_lib/__tests__/
allow: frontend/apps/storefront/app/(account)/orders/[orderId]/page.tsx
allow: frontend/apps/admin/app/(dash)/orders/[orderId]/page.tsx
allow: frontend/apps/admin/app/(dash)/orders/_lib/

> `(checkout)/_components/__tests__/` 是新目錄、`(checkout)/_lib/cvsSelection.ts` 是新檔。`types.admin.ts` 預期只多三個門市欄位，多了別的就停下來回報。
> 前台訂單詳情只加地址一行、後台訂單詳情只加「取貨門市」一格。`docs/` 全域放行，但契約 YAML、`docs/05`、`docs/00` 不准動。
-->

---

<!--
★ 2026-09-15 已通過整合驗收並提交（前端 `b7250db`），FE-31 與 FE-32 一起撤包。原文保留供追溯。

FE-31（Claude Sonnet）一輪：`(info)/` 三頁 ＋ `InfoLinks`／`InfoPageFooter`、首頁頁尾與「我的」頁的入口、`infoPages.test.ts` 26 條（四類承諾反例注入確認會紅）。
FE-32（Codex gpt-5.6-sol）一輪：api-client 重生（`types.admin.ts` 零 diff）＋ 三支端點 ＋ mock；`_lib/favorites.ts` 共用狀態機；立即購買 `executeCartIntent` 同步鎖 ＋ 去處純函式；cart 頁提示；`/favorites`；未定價卡片補愛心；四組違規探針確認會紅並還原。
測試 500 → **558**。Leader 驗（兩包都交付後序列跑）：`pnpm --recursive typecheck` EXIT=0、558 全過（api-client 40／admin 132／storefront 386）、`pnpm --filter storefront build` EXIT=0（路由表含 /faq /guide /about /favorites）、audit 通過；dev 環境 SSR：首頁三個資訊連結、三頁 200 且標題不重複、互連與回首頁、登入客戶 A 的商品頁愛心為「取消收藏」、客戶 B 不是。
⚠ 真瀏覽器逐一點擊的畫面走查**尚未做**（Chrome 擴充未連線）；client 端互動由純函式測試覆蓋，待補。
留下：`ProductCardLink` 每張卡各渲染一個 fixed 錯誤 Toast 容器；`/favorites` 的 `<title>` 只有「GreyGray」（client component 沒有 metadata）；客服聯絡方式待使用者提供。

## 生效中（已撤包）：FE-31　資訊頁——常見問題／購買流程／關於我們，以及它們的入口與出口

使用者 2026-09-15 以 `/goal` 下達「常見問題 Q&A、關於我們、購買流程簡介之類的一些小東西」。前台現在沒有任何資訊頁，首頁沒有頁尾。
三頁放新 route group `(info)`；**入口**是首頁頁尾（匿名唯一入口）與「我的」頁的「幫助與資訊」區塊，**出口**是每頁底部互連 ＋ 分頁列（黑名單規則，新頁預設就有）。
文案 Leader 已依已確認事實比對過（派工書 §1.4）。同一波 FE-32（同一棵樹）、後端 BE-52 平行。

★★ 最容易做錯的：① **不准自己補事實**（客服電話／LINE／地址／營業時間／出貨天數都沒確認，不要做空的「聯絡我們」）；
② 入口與出口要用**掃原始碼的測試**釘住，掃到零個算失敗；③ 不動 `tabs.ts`／`topBar.ts`／`_lib/__tests__/`（FE-32 的範圍）；
④ 「我的」頁加的「我的最愛」連結指向 FE-32 做的頁，同一波驗收，不要做假頁；⑤ 不跑 `next build`、不起停 dev server。

package: FE-31
doc: docs/34-前端第二十波派工書.md
allow: frontend/apps/storefront/app/(info)/
allow: frontend/apps/storefront/app/(shop)/page.tsx
allow: frontend/apps/storefront/app/(account)/me/page.tsx

> `(info)/` 是新目錄。`(shop)/page.tsx` 只加頁尾區塊、`me/page.tsx` 只加區塊與一筆連結，不改既有版面與邏輯。

---

## 生效中：FE-32　立即購買 ＋ 最愛清單前端（ADR-036／ADR-037）

使用者 2026-09-15 以 `/goal` 下達「可以新增立即購買」「最愛清單——可以加上我的最愛瀏覽」。現在的愛心是假的（`ProductCardLink.tsx` 第 18 行、`AddToCartPanel.tsx` 第 36 行本地 state）。
契約三條最愛端點 Leader 已從後端樹逐位元複製進來；後端 BE-52 同一波平行實作。立即購買契約零改動：加進購物車後，車裡只有這一次加的 → `/checkout`，否則 → `/cart?from=buy-now` 並提示。

★★ 最容易做錯的：① **連點與搶按要用同步 `pendingRef` 在任何 `await` 之前鎖住**——`setState('loading')` 不是鎖，`usePayloadIdempotency` 也不防同時送兩個；
② 最愛切換抽成**一份**共用邏輯（樂觀更新、失敗還原、`401` 先還原再導去登入）；③ `ProductCardLink` 的 **`priceFrom: null` 分支也要有愛心**（收藏清單裡會有未定價商品）；
④ 型別一律 `pnpm api:generate`，**不准手寫**，`types.admin.ts` 預期零 diff；⑤ cart 頁讀 `from=buy-now` 不准用 `useSearchParams()`；
⑥ 不動 `packages/ui`、`InfiniteProductGrid`、`me/page.tsx`、契約；不加快取設定（SSR 快取已查證不外洩，派工書 §2.1）。
★ 兩包同一棵樹：開發中只跑自己的測試檔，交付前跑一次完整 recursive、失敗在別包的檔就原樣記下不重跑；權威驗證由 Leader 兩包都交付後序列跑（派工書 §0.3）。

package: FE-32
doc: docs/34-前端第二十波派工書.md
allow: frontend/packages/api-client/src/types.storefront.ts
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/endpoints/storefront.ts
allow: frontend/packages/api-client/src/endpoints/README.md
allow: frontend/packages/api-client/src/mock/
allow: frontend/apps/storefront/app/(shop)/products/[productId]/
allow: frontend/apps/storefront/app/(shop)/_components/ProductCardLink.tsx
allow: frontend/apps/storefront/app/(shop)/_components/__tests__/
allow: frontend/apps/storefront/app/(account)/favorites/
allow: frontend/apps/storefront/app/_lib/favorites.ts
allow: frontend/apps/storefront/app/_lib/tabs.ts
allow: frontend/apps/storefront/app/_lib/__tests__/
allow: frontend/apps/storefront/app/(checkout)/cart/page.tsx
allow: frontend/apps/storefront/app/(checkout)/_lib/buyNowNotice.ts
allow: frontend/apps/storefront/app/(checkout)/_lib/__tests__/buyNowNotice.test.ts

> `(account)/favorites/`、`_lib/favorites.ts`、`(checkout)/_lib/buyNowNotice.ts` 與它的測試是新的。`types.admin.ts` 在 allow 裡只因為 codegen 會一起重寫，有 diff 就停下來回報。
> `products/[productId]/page.tsx` 的 SSR 內容不動；`tabs.ts` 只加 `/favorites` 一個字串；`cart/page.tsx` 只加 `from=buy-now` 提示；`_lib/__tests__/pageShell.test.ts` 不改。
-->

---

<!--
★ 2026-09-06 已通過整合驗收並提交（前端 `398f37a`），撤包。原文保留供追溯。

一輪。出貨單列表「訂單數」→「訂單」欄（顯示訂單編號並連到 `/orders/{id}`；單一 id 查不到退回 `id.slice(0,8)`，**整份清單讀取失敗才**退回「N 張」——兩者是不同狀況）；訂單列表加「出貨進度」欄，**重用 FE-29 的 `summarizeOrderShipments`**、沒有複製一份，一次 `limit:100` 前端比對不做 N+1，已取消顯示「已取消」，有下一頁時附「（可能不完整）」。測試 481 → **500**（新增 19，涵蓋七種情況）。
Leader 驗：`pnpm --recursive typecheck` 全綠、測試 500（api-client 35／admin 132／storefront 333）、`pnpm --filter admin build` 成功、audit 十一項通過（⑪ 零候選）、`git diff` 逐行（契約與 `packages/*` 零改動）。
留下：「出貨進度」欄沒有排序／篩選；欄位位置（狀態之後、所屬團之前）是子代理的判斷，Leader 同意。

## 生效中：FE-30　兩張列表頁補上「這張出貨單掛哪張訂單」（#48）與「這張訂單的出貨進度」（#49）

使用者 2026-09-04 明確說這兩件放優化，但它們是**會產生錯誤業務資料**的操作面缺口：那天兩張相隔四秒建立的出貨單日期／配送方式／狀態全一樣，
使用者**真的把交運按在別張訂單的出貨單上**。`shipments/page.tsx` 第 117-125 行的欄位只有「N 張（合併出貨）」沒有編號；
`orders/page.tsx` 第 123-170 行的欄位完全沒有出貨資訊——FE-29 只做了訂單詳情頁那一側。

★ 判斷用的純函式 FE-29 已經寫好而且有測試：`shipments/_lib/orderShipments.ts`（`shipmentsOfOrder` 第 46 行、`summarizeOrderShipments` 第 53 行、
`orderShipmentSummaryText` 第 76 行、`countShipmentsByOrderId` 第 111 行）。**這一包重用，不准複製一份**。
資料照 `shipments/[shipmentId]/page.tsx` 第 46-49／70-76 行既有的模式（`limit: 100` 一次抓、前端組 map、拿不到就退回原始 id 且不擋整頁），**契約零改動**。

★★ 最容易做錯的：① 訂單 map 查不到要退回 `id.slice(0,8)`，不是空白；② 一張出貨單可以掛多張訂單，`orderIds` 是陣列；
③ **不要每一列各打一次 API**（訂單列表有分頁，那是 N+1）；④ `limit: 100` 只有第一頁，不要顯示會騙人的總數；
⑤ 已取消的訂單不要顯示會誤導的進度文字。

package: FE-30
doc: docs/33-前端第十九波派工書.md
allow: frontend/apps/admin/app/(dash)/orders/
allow: frontend/apps/admin/app/(dash)/shipments/

> `docs/` 全域放行。契約、`packages/*`、前台 `apps/storefront`、`shipments/[shipmentId]/page.tsx` 的交運／送達流程不在範圍——派工書 §2 說明過。

-->
---

<!--
★ 2026-09-04 已通過整合驗收並提交（前端 `b241f48`），撤包。原文保留供追溯。

一輪。`shipments/_lib/orderShipments.ts`（新檔，純函式：摘要、文案、可勾選訂單、每張訂單的出貨單張數——訂單頁與對話框共用同一份規則）；`orders/_components/OrderShipmentsSection.tsx`（新檔，純呈現）＋訂單頁掛上去（獨立錯誤狀態、讀失敗不擋整頁）；`CreateShipmentDialog` 濾掉已出貨／已完成／已取消、已有出貨單的仍可勾但標「已有 N 張」、頂端加 N:M 說明、空清單分兩種說法。測試 461 → **481**（純函式 12 ＋ renderToStaticMarkup 8）。
「簽收」只認 `Delivered`，`Returned`／`Lost` 一樣算「還沒簽收」（與後端比對條件一致）。
Leader 驗：typecheck 四專案全綠、481 條全過、audit 通過、契約與 `packages/*` 零改動；與後端 BE-48 同一輪部署（release `20260904015905167`）。
留下三個追蹤：`limit: 100` 只拿第一頁（訂單頁有提醒、對話框沒有）；`Returned`／`Lost` 畫面只說「還沒簽收」、不會說「要重開一張」（要先定業務動作）；訂單**列表**頁沒加（派工書只點名詳情頁）。

## 生效中（已撤包）：FE-29　讓「這張訂單掛了幾張出貨單、還差幾張沒簽收」看得見（#45）

2026-09-04 00:51 使用者回報「宅配到府那邊也不會更新，交運還是已送達都不會」。Leader 查證：**不是配送方式的問題**。訂單 `GG2609036EC92E5` 底下有三張出貨單（一張建了沒交運、一張已送達、一張只交運），而 ADR-025 的規則是掛在同一張訂單上的出貨單**全部簽收**才轉「已出貨」——所以訂單留在「待出貨」是正確的，只是畫面完全沒講。
三張是怎麼長出來的：`CreateShipmentDialog.tsx:51` 用 `listOrders(..., limit: 50)` 列訂單、**沒有任何狀態過濾**，已出完貨甚至已取消的訂單照樣可以再勾，也沒顯示「這張已經有幾張出貨單」。訂單頁（`orders/[orderId]/page.tsx`）則完全沒有出貨單的資訊。
派工書 `docs/32-前端第十八波派工書.md`。**契約一個字都不用改**：`GET /v1/orders` 已支援 `status`，`AdminShipment` 有 `orderIds` 可在前端過濾（同一支 `shipments/[shipmentId]/page.tsx` 第 46-49、70-73 行就是這個模式）。

★★ 最容易做錯的：① 已經有出貨單的訂單**仍然可以勾**（拆單合法），只要標示，不要直接拿掉；② 「還差幾張」的數字要算出來、不要寫死；③ 標籤與 tone 用 `shipments/_lib/labels.ts` 現成的，不要重寫一份；④ 新區塊讀取失敗不能把整頁變錯誤頁；⑤ 沒有 jsdom，判斷抽純函式測、文案用 `renderToStaticMarkup`。

package: FE-29
doc: docs/32-前端第十八波派工書.md
allow: frontend/apps/admin/app/(dash)/orders/
allow: frontend/apps/admin/app/(dash)/shipments/

> `docs/` 全域放行。`packages/*`、`apps/storefront`、契約 YAML 不在 allow——派工書 §2 說明過；真的需要就停下來回報。測試放在上面兩個目錄底下的 `_lib/`／`__tests__/`。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（前端 `5b01314`），撤包。原文保留供追溯。

兩輪。`subtotalPreview` 純函式（三個守衛，含乘積非安全整數 throw；全 repo 唯一呼叫端 `BottomBarSummary`）、`BottomBarSummary`（「N 件 · 單價」／「小計 NT$X」）、`UnitPriceBlock`（面板頂端單價；沒價格顯示 `noPriceMessage`）、
`AddToCartPanel` 只傳 state、`frontend/README.md` 第 2 條例外註記。第二輪：沒定價的說明只在資訊區說一次（預購「售價在開團時決定，開團後才能加入購物車。」／現貨「尚未定價」），`disabledReason` 在 `!hasPrice` 回 null。
測試 436 → **461**（storefront 333）、typecheck 全綠、Leader 重跑相符；audit 通過。畫面在正式站部署後驗。

## 生效中（已撤包）：FE-28　商品詳情頁價格顯示（#40）——底部列「N 件 · 小計」（ADR-033 唯一前端乘法例外）＋ 商品資訊區「單價」

使用者 2026-09-03 11:34 在正式站付款走通後截圖回報：底部列只有單價、選 5 件看不到總額；資訊區沒有單價。使用者拍板「前端乘，當小計預覽」→ ADR-033（唯一例外，只用於顯示、購物車以後端為準）。

★★ 最容易做錯的：① 小計只准商品頁用，購物車／結帳／訂單頁的金額仍全部來自後端；② 純函式 `subtotalPreview` 一處，非法數量／金額 throw 不湊 0；③ 不動 `packages/ui`、`packages/api-client`、契約；④ 沒有 jsdom——「5 件 → NT$300」靠純函式＋純呈現元件測；⑤ dev server 由 Leader 管，不停、不起、不跑 `next build`。

package: FE-28
doc: docs/31-前端第十七波派工書.md
allow: frontend/apps/storefront/app/_lib/subtotalPreview.ts
allow: frontend/apps/storefront/app/_lib/__tests__/subtotalPreview.test.ts
allow: frontend/apps/storefront/app/(shop)/products/[productId]/
allow: frontend/README.md

> `docs/` 全域放行；`page.tsx` 在 allow 的目錄裡但派工書 §2 說 SSR 內容不動——只准動 `_components/` 與 `__tests__/`，`page.tsx` 有 diff 就停下來回報。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（前端 `56a221d`），撤包。原文保留供追溯。

一輪交付。api-client：重生型別（`types.admin.ts` ＋54、`types.storefront.ts` 零 diff）、`createSku`／`listLots`／`createLot`（lot body 從 `paths['/v1/lots']['post']` 推導，沒有手寫）、mock 三條＋4 smoke；
商品頁：SKU 區抽成 `SkuSection`（「新增 SKU」只在 canWrite；列上「編輯」／「進貨」）、`SkuEditDrawer` 新增／編輯共用（`isActive` 預設 true、Stock 或既有標價才顯示標價欄）、`LotDrawer` 新檔（批號列表＋進貨表單）、`skuForm.ts`／`lotForm.ts` 純轉換（沒有 jsdom 才拆得出真測試）。
測試 410 → **436**（api-client 35、admin 93、storefront 308）、typecheck 全綠、Leader 重跑相符；audit 十一項通過。子代理擔心的 `POST /v1/lots` 後端早在 BE-38 就有。
Leader 接受四個自主判斷（多開純函式檔、預購 SKU 沒填過標價就隱藏欄位、進貨鈕不限 Stock、批號列表不分頁）；追蹤：契約 `isActive` 沒列 required 但生成器產成必填、`/v1/lots` 契約只宣告 201、前後台各一份 fieldErrors helper。畫面驗收在正式站做（build.ps1 -Publish 的 admin build 就是這一包的 build 驗收）。

## 生效中（已撤包）：FE-27　後台商品頁「新增 SKU」（新端點 `POST /v1/products/{productId}/skus`，ADR-032）＋ SKU 列「進貨」與批號列表（`/v1/lots`）

使用者 2026-09-03 08:51 在正式機後台建了商品，SKU 區只有空狀態「契約目前只有 PATCH…沒有新增 SKU 的端點」；而且就算有 SKU，後台也沒有進貨頁（M2 `POST /v1/lots` 只有 API）。
使用者拍板「不種，等正式做法」→ ADR-032。後端 BE-44 已把新端點做進契約（`docs/api/openapi.admin.yaml` 與 `docs/05` 是 Leader 從後端樹逐位元複製進來的）；這一包補兩個 UI 缺口。

★★ 最容易做錯的：① 不要改 `docs/api/*.yaml`、`docs/05`（稽核第 ⑦ 項會擋）；② 型別一律 `pnpm api:generate` 重生後從 `S['…']` 拿，**不准手寫請求型別**（FE-26 那個手寫複本是追蹤項，不要再加一份）；
③ `types.storefront.ts` 會被 codegen 一起重寫，預期零 diff；④ 金額輸入 NT$ → `amountMinor` 的轉換照 `SkuEditDrawer` 現貨標價既有的 helper，不另寫；⑤ dev server 由 Leader 管——不停、不起、不跑 `next build`，活體驗收是 Leader 的事。

package: FE-27
doc: docs/30-前端第十六波派工書.md
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/types.storefront.ts
allow: frontend/packages/api-client/src/endpoints/admin.ts
allow: frontend/packages/api-client/src/endpoints/README.md
allow: frontend/packages/api-client/src/mock/
allow: frontend/apps/admin/app/(dash)/catalog/

> `types.storefront.ts` 在 allow 裡只是因為 codegen 會一起重寫它——預期零 diff，有 diff 就停下來回報。
> `apps/admin/app/(dash)/catalog/` 整個目錄放行：商品頁、`_components/`（`SkuEditDrawer.tsx` 改、`LotDrawer.tsx` 新增）、測試都在裡面；側邊欄不在 allow（這一包不加新頁面，進貨做在商品頁的抽屜裡）。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（前端 `7f052c5`），撤包。原文保留供追溯。

測試 376 → **410**、typecheck／build EXIT=0（build 由 Leader 停前台 dev server 後跑）。Leader 真瀏覽器（匿名、只按畫面）：純預購購物車送出 → 直接到 `/login?next=%2Fcheckout`
（不再 500，BE-41＋FE-26 合起來的結果）→ 分頁列亮「購物車」→ `sessionStorage` 有 `gg:checkout-draft:<cartId>`（五個欄位）→ 從購物車回到結帳：**超商取貨、門市代號 12321、留言都還原、運費重新試算 NT$60**。
登出徽章歸零需要登入，留給使用者走。兩輪：第一輪 A 卡在手寫的 `CheckoutRequest`（`endpoints/storefront.ts`）、C 呼叫端 `StorefrontTabBar` 不在 allow——都對，Leader 補授權（`5696606`）後第二輪收尾：
改一行手寫型別、拿掉 `!`（零命中）、分頁列掛載後讀 `window.location.search`（不用 `useSearchParams`，避免根 layout CSR bailout）。
留下：整組請求型別都是契約的手寫複本，契約改了不會有東西說話（建議改成從 `paths[...]` 推導）；草稿只在「按了送出才知道要登入」那條路上存。

## 生效中（已撤包）：FE-26　跟上 ADR-030 契約（重生型別、拿掉 `!`）＋ #36 前端側 ＋ FE-25 ⑦ ＋ 結帳頁登入回跳保留已填內容

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
allow: frontend/packages/api-client/src/endpoints/storefront.ts
allow: frontend/apps/storefront/app/_components/StorefrontTabBar.tsx

> `types.admin.ts` 在 allow 裡只是因為 codegen 會一起重寫它——預期零 diff，有 diff 就停下來回報。
> 第一輪交付後 Leader 補授權（2026-09-03）：`endpoints/storefront.ts` 只准改 `CheckoutRequest.shippingPolicy` 那一行（手寫複本跟上契約，`?: … | null`），
> `StorefrontTabBar.tsx` 只准把查詢字串交給 `activeTabHref`——**不用 `useSearchParams()`**（根 layout 元件會被推進 CSR bailout），
> 比照 FE-25 `authRedirect.ts` 的做法：掛載後讀 `window.location.search`，SSR 首次渲染仍只看 pathname。
-->

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

- **FE-33** 結帳頁用 7-ELEVEN 電子地圖選門市（ADR-038）：`cvsSelection.ts` 回程合併與錯誤分支、`ConvenienceStoreField` 隱藏表單自動送出、結帳只送選店票、前台訂單詳情地址、後台「取貨門市」　·　2026-09-15 通過　·　`ff132af`　·
  測試 558 → 643；與 BE-53 合起來 HTTP 旅程 32 項驗過；真瀏覽器畫面走查待補，見 `.dispatch/reports/FE-33.md`
- **FE-32** 立即購買 ＋ 最愛清單前端（ADR-036／ADR-037）：三支最愛端點與共用樂觀切換、商品頁「立即購買」同步鎖與去處判斷、`/favorites` 頁、cart 頁提示、未定價卡片補愛心　·　2026-09-15 通過　·　`b7250db`　·
  與 FE-31 同一個 commit；測試兩包合計 500 → 558；真瀏覽器畫面走查待補，見 `.dispatch/reports/FE-32.md`
- **FE-31** 資訊頁：常見問題／購買流程／關於我們（`(info)/`）＋ 首頁頁尾與「我的」頁的入口、每頁互連出口　·　2026-09-15 通過　·　`b7250db`　·
  文案只用已確認事實（無客服聯絡方式、無鑑賞期、無出貨時限），見 `.dispatch/reports/FE-31.md`
- **FE-30** 出貨單列表看得出掛哪張訂單（#48）＋ 訂單列表看得出出貨進度（#49）　·　2026-09-06 通過　·　`398f37a`　·
  測試 481 → 500，見 `.dispatch/reports/FE-30.md`
- **FE-29** 讓「這張訂單掛了幾張出貨單、還差幾張沒簽收」看得見（#45）：訂單頁加出貨單區塊並講出還差幾張、建立出貨單時濾掉已出貨／已完成／已取消、已有出貨單的標示但仍可勾　·　2026-09-04 通過　·　`b241f48`　·
  測試 461 → 481；規則抽成純函式讓訂單頁與對話框共用；與後端 BE-48 同一輪部署；見 `.dispatch/reports/FE-29.md`
- **FE-28** 商品詳情頁價格顯示（#40）：底部列「N 件 · 小計」（`subtotalPreview`，ADR-033 唯一前端乘法例外）＋ 資訊區「單價」；沒定價的說明只說一次　·　2026-09-03 通過　·　`5b01314`　·
  測試 436 → 461；兩輪；使用者在正式站付款走通後截圖回報的，見 `.dispatch/reports/FE-28.md`
- **FE-27** 後台商品頁「新增 SKU」（`POST /v1/products/{productId}/skus`，ADR-032）＋ SKU 列「進貨」抽屜與批號列表（`/v1/lots`）；api-client `createSku`／`listLots`／`createLot` 全從契約重生　·　2026-09-03 通過　·　`56a221d`　·
  測試 410 → 436、typecheck 全綠；#39（正式機後台建商品後沒有任何合法路徑建出第一個 SKU）前端側；畫面驗收在正式站做，見 `.dispatch/reports/FE-27.md`
- **FE-26** 跟上 ADR-030 契約（重生型別、拿掉 `shippingPolicy!`）＋ 登出徽章歸零（#36 前端側）＋ `/login?next=` 亮對分頁（FE-25 ⑦）＋ 結帳頁登入回跳保留已填內容　·　2026-09-03 通過　·　`7f052c5`　·
  測試 376 → 410；Leader 真瀏覽器：純預購匿名送出 → 登入頁（不再 500）、分頁列亮購物車、回到結帳欄位與運費都還原，見 `.dispatch/reports/FE-26.md`
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
