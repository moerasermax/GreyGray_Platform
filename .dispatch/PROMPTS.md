# 啟動 prompt

**目前沒有生效中的派工（2026-09-19）。** 第四十波五包全部驗收撤包：

- 後端 **BE-54**（收件人姓名／手機／宅配地址凍結進訂單、後台明文、outbox 保存期限，migration `0021`）
  ＋ **BE-55**（客服工單新模組 `CustomerService`、匿名留言端點、後台列表，migration `0022`）
  —— 同一個 commit `5637409`
- 前端 **FE-34** `8fb5f34`＋`06647c6`、**FE-35** `04a1510`、**FE-36** `e21930d`＋`68d98a2`

測試：後端 Release 13 個專案逐一前景跑完（結果見 `GreyGray_PM/03-驗收紀錄.md` 2026-09-19）、
前端 654 → **768**。契約正式異動見 ADR-039／ADR-040 與 `docs/05` 的異動紀錄。

⚠ **這一波還沒 push、還沒部署**（第三十八～四十波都沒上正式機）。
部署時正式機要套 migration `0018`～`0022`，並在 Storefront 放 `Logistics:ECPay:*` 物流設定。

★ 下一波開工前要知道的：

- **#59 還開著**：`src/Shared.Kernel/Json/GuidIdJsonConverter.cs` 的
  `GuidIdJsonConverterFactory.CanConvert` 會把裸 `Guid?` 誤判成 `XxxId`，丟 `TypeLoadException`。
  BE-55 用 wrapper 型別繞過了，**根因沒修**。任何契約型別只要用裸 `Guid?` 就會踩到，
  而且錯誤訊息完全看不出跟 `Guid?` 有關。值得開一個小包修。
- **`ops/` 的清單是人工維護的**：#58 這一波補了 migration 清單三處，
  但「加東西要記得同步 hardcode 清單」這個形狀還在（schema 清單、
  `verify-environment.ps1` 的期望值、E2E 測試裡的陣列）。
- ⚠ 派需要跑後端測試的包之前先 `docker info`；子代理跑 audit 用
  `C:\Program Files\Git\bin\bash.exe` 明確路徑。
- ⚠ **長測試要在派工 prompt 裡明講「留在這一輪等它跑完，不要掛背景就結束」**——
  第四十波兩個後端包都因此交付到一半就結束，要用 `session_id` 恢復。
- ⚠ 子代理跑 Testcontainers 會讓 dev 的 `greygray-dev-postgres` 容器整個消失，
  重建時第一次會因 crash recovery 超過 60 秒逾時而失敗，等 `pg_isready` 通了再跑一次。
- ⚠ Leader 自己重跑後端測試：**直接跑 `tests\<專案>\bin\<Configuration>\net10.0\<專案>.exe`**；
  `dotnet test` 在 .NET 10 會走 VSTest 全數報錯、`--project` 是未知參數——兩次都 exit 0 但一條沒跑。

以下保留第四十波五包的啟動 prompt 供參考。

---

## BE-54 的啟動 prompt

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動 Checkout／Ordering 的收件人快照、兩個 Host 的 M1aEndpoints.cs、Platform/Outbox 的保存期限、一支新 migration（0021）、tests。契約 Leader 已寫好，不准改。沒有前端。

GG_PACKAGE=BE-54

開工前務必先讀：
  CLAUDE.md                                六條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分、§6 邊界測試
  docs/00-decisions.md                     ADR-039（收件人資訊）、ADR-038（門市凍結五段，照抄它）
  docs/54-後端第三十八波派工書.md            ★ 整份讀完
  docs/api/openapi.storefront.yaml         CheckoutRequest／Order 的新欄位（不准改）
  docs/api/openapi.admin.yaml              AdminOrder 的新欄位與 customerContactMasked 描述（不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-54.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 只准寫 .dispatch/ACTIVE.md 裡 BE-54 的 allow 路徑。缺授權停下來回報，不要自己改 .dispatch/。

★★ 這一包最容易做錯的六件事（派工書 §1 有完整說明）：
  ① 宅配不要叫客人填——ValidateDeliveryAsync 宅配分支已經取到地址，在那裡抄一份
  ② Cart.Complete 的 Completed* 那一段不要漏（漏了事件重放時快照變 null）
  ③ 冪等指紋一定要加這兩個欄位；加了之後同鍵換收件人回 422 platform.idempotency-key-reused，這是要的，補測試
  ④ 前台 OrderResponse 的收件人從訂單快照取，不准從即時回查的地址簿取
  ⑤ outbox 清理不刪 IsDeadLettered 的
  ⑥ ops/、GreyGray.slnx、架構測試是 BE-55 的，碰了會撞

做完跑 ops	est.ps1 與 ops\check-openapi.ps1，結果貼進報告。
```

---

## BE-55 的啟動 prompt

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包開一個新模組 CustomerService（Contracts/Core/Infra）、兩個 Host 各一支新的 SupportEndpoints.cs、一支新 migration（0022，含新 schema 與 login role）、三個 ops 腳本與兩個 E2E 測試的 schema 清單、架構測試、slnx。契約 Leader 已寫好，不准改。沒有前端。

GG_PACKAGE=BE-55

開工前務必先讀：
  CLAUDE.md                                六條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分
  docs/00-decisions.md                     ADR-040（客服工單）、ADR-017（支撐模組的界線）
  docs/55-後端第三十八波派工書-第二包.md      ★ 整份讀完
  docs/api/openapi.storefront.yaml         POST /v1/support/tickets、SupportTicket（不准改）
  docs/api/openapi.admin.yaml              三條後台端點、SupportTicketPage（不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-55.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 只准寫 .dispatch/ACTIVE.md 裡 BE-55 的 allow 路徑。缺授權停下來回報。

★★ 這一包最容易做錯的六件事：
  ① 端點寫在自己的新檔 SupportEndpoints.cs，不准改 M1aEndpoints.cs（BE-54 正在改，一定撞）
  ② 五處 hardcode schema 清單（三個 ops 腳本 ＋ 兩個 E2E 測試）漏一個就 dev 起不來或 CI 紅；自己 grep 確認，不要照抄派工書的行號
  ③ 防灌的 fail-open 要自己包 try-catch——IDistributedCache 斷線會拋例外，照抄就變成 Garnet 一掛連正常留言都 500
  ④ 模組叫 CustomerService 不叫 Support；但端點路徑就是 /v1/support/tickets，這個不一致是刻意的
  ⑤ 不要把它加進架構測試的 SupportModules 清單（那是「支撐模組」的意思）
  ⑥ 前台沒有查詢工單的端點，不要自己補

做完除了 ops	est.ps1，★ 一定要自己起一次 dev 環境證明沒把它弄壞：
  ops\install-dev-environment.ps1
  ops\start-dev-hosts.ps1 -Configuration Release -UseEcpaySimulator
把 PASS 行貼進報告。起 Host 一律用新的 shell（#38：同一個 shell 裡起→停→再起，第二次環境變數會是空的）。
```

---

---

## FE-34 的啟動 prompt

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray 前端（Next.js App Router，pnpm workspace）。這一包動結帳頁的收件人欄位、前台訂單詳情、後台訂單詳情、api-client 的型別。契約 Leader 已寫好（後端樹 docs/api），不准改。後端 BE-54 平行進行中，不要等它。

GG_PACKAGE=FE-34

開工前務必先讀：
  CLAUDE.md                                前端四條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分
  docs/36-前端第二十二波派工書-第一包.md      ★ 整份讀完
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-34.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 只准寫 .dispatch/ACTIVE.md 裡 FE-34 的 allow 路徑。缺授權停下來回報。

★★ 這一包最容易做錯的五件事：
  ① 草稿還原——選門市會跳出站外再回來，新欄位一定要進 currentDraft() 與 loadCheckoutDraft 的還原流程，漏了客人回來欄位就空了。要有測試
  ② 宅配模式不顯示也不送這兩個欄位（後端從地址簿抄）；只有超商取貨才顯示
  ③ 手機要檢查 09 開頭十碼，比既有的 validateAddressForm 嚴（超商會擋，錯了客人拿不到貨）
  ④ 後台拿掉 MaskedContactNote.tsx，不要做「點一下看明文」的按鈕——使用者明確不要那道摩擦
  ⑤ app/layout.tsx、app/_components/、packages/ui/ 是 FE-35 的，app/(info)/ 是 FE-36 的，碰了會撞

360px 與「頁面有沒有出口」是硬性檢查項。做完跑 test／typecheck／lint／build，結果貼進報告。
```

---

## FE-35 的啟動 prompt

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray 前端（Next.js App Router，pnpm workspace）。這一包做前台右下角的客服小幫手（引導式選單、不接 AI）與後台「客服訊息」頁。契約 Leader 已寫好（後端樹 docs/api），不准改。後端 BE-55 平行進行中，不要等它。

GG_PACKAGE=FE-35

開工前務必先讀：
  CLAUDE.md                                前端四條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分
  docs/37-前端第二十二波派工書-第二包.md      ★ 整份讀完
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-35.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 只准寫 .dispatch/ACTIVE.md 裡 FE-35 的 allow 路徑。缺授權停下來回報。

★★ 這一包最容易做錯的六件事：
  ① 不接 AI、不接第三方客服套件（ADR-040 明確否決）
  ② 選項與答案重用 app/(info)/_content/faq.ts 的 FAQ_GROUPS，不要另抄一份（FE-36 正在改那些字，抄了會走鐘）；那個檔你只能 import 不能改
  ③ 不能擋住結帳頁的送出鈕與購物車的結帳鈕；360px 下關閉鈕一定要看得見、按鈕不要疊在分頁列上
  ④ 不要改 packages/api-client/（FE-34 正在動），客服的型別與呼叫寫在自己的 _lib 裡
  ⑤ 前台不做「查詢我的工單」頁——匿名工單沒有安全的查詢方式，這是刻意的
  ⑥ packages/ui/ 是兩個 app 共用的，改動要向下相容；不確定就用包裝，不要改既有元件的 API

做完跑 test／typecheck／lint／build，結果貼進報告。
```

---

## FE-36 的啟動 prompt

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray 前端（Next.js App Router，pnpm workspace）。這一包新增服務條款頁並改 FAQ 的鑑賞期與客服文案。完全獨立，不依賴任何後端改動。

GG_PACKAGE=FE-36

開工前務必先讀：
  CLAUDE.md                                前端四條鐵則 ＋ 派工規則
  docs/00-decisions.md（後端樹）             ADR-025（鑑賞期七天、法律適用由老闆判斷）
  docs/38-前端第二十二波派工書-第三包.md      ★ 整份讀完，條款全文在 §1.4，照抄
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-36.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 只准寫 .dispatch/ACTIVE.md 裡 FE-36 的 allow 路徑（只有 app/(info)/）。缺授權停下來回報。

★★ 這一包最容易做錯的五件事：
  ① 條款第五、六節有兩個「老闆確認項」，原樣保留成看得見的提醒，不要自己刪、不要自己判斷法律適用
  ② 三處「請聯絡客服」改成指向右下角小幫手，不要寫死 LINE／Email／電話（使用者還沒給，寫了就是假的）
  ③ 不要改 FaqGroup／FaqItem 的型別或匯出名字——FE-35 要 import FAQ_GROUPS
  ④ FAQ 只寫指路，法律細節一律放服務條款一處，不要兩邊各寫一份會走鐘
  ⑤ 這一頁要有出口（曾經有三頁一個出口都沒有），360px 下長文不要橫向捲動

做完跑 test／typecheck／lint／build，結果貼進報告。
```

---

---

以下保留上一波（第三十九波，已撤包）的標頭與啟動 prompt 供參考。

**（派工時）第三十九波（2026-09-15）——兩包平行、跨兩棵樹、`allow` 零重疊。**
後端 **BE-53**（7-ELEVEN 選店票、綠界回傳驗證、門市凍結進訂單、後台門市欄位、模擬器假地圖，`docs/52`）＋ 前端 **FE-33**（結帳頁電子地圖選門市、前後台訂單詳情顯示門市，前端樹 `docs/35`）。
使用者以 `/goal` 問「可以新增 7-11 收貨嗎」。契約（三條 `/v1/logistics/*`、`CheckoutRequest.convenienceStoreSelectionId`、`Order.convenienceStoreAddress`、`AdminOrder` 三個門市欄位）與 ADR-038 由 Leader 寫好並逐位元複製進前端樹。
兩份派工書經 Codex `gpt-5.6-sol` 逐行覆驗（後端 13 條、前端 13 條）與 Gemini `3.1-pro-high` 情境覆驗（10 條），採納的已改進派工書。
測試基準：後端 **340**、前端 **558**。實作者分派：BE-53 給 Codex `gpt-5.6-sol`＋high，FE-33 給 Claude `opus`＋medium（跨家）。
★ 後端樹 dev Host 與綠界模擬器現在**有在跑**（上一波驗收留下的），子代理不要起停。Docker Desktop 在跑。
★ 子代理跑 audit 用 `C:\Program Files\Git\bin\bash.exe` 明確路徑（裸 `bash` 會命中 WSL stub）。

---

## BE-53 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動 Storefront Host（新 Logistics/）、Checkout／Ordering 的門市快照、Admin 訂單回應、一支新 migration（0020）、綠界模擬器假地圖、ops/start-dev-hosts.ps1 一段、tests。契約 Leader 已寫好，不准改。沒有前端。

GG_PACKAGE=BE-53

開工前務必先讀：
  CLAUDE.md                                六條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分、§6 邊界測試
  docs/00-decisions.md                     ADR-038（7-ELEVEN 電子地圖）、ADR-029（綠界模擬器與網域守衛）
  docs/52-後端第三十七波派工書.md            ★ 整份讀完：§0 事實（所有行號）＋ §1 必做 ＋ §2 不要做 ＋ §3 所有權 ＋ §5 可能寫錯的地方
  docs/api/openapi.storefront.yaml         logistics 三條 operation、CvsMapSession、CvsStoreSelection、CheckoutRequest、Order（不准改）
  docs/api/openapi.admin.yaml              AdminOrder 三個門市欄位（不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-53.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     docker info（沒在跑就停下來回報）
     dotnet build .\GreyGray.slnx -c Debug --nologo（0 警告 0 錯誤；不要跑 ops\build.ps1）
     測試分專案前景跑，基準 340（含 2 個既有 Skip）＋ 你新增的；GreyGray.Architecture.Tests 必須在裡面、Total: 單獨貼出
     ops\check-openapi.ps1 -Configuration Debug（Storefront M1a 29 → 32、Admin 30）
     & 'C:\Program Files\Git\bin\bash.exe' .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 綠界地圖的回傳沒有檢查碼、而且是從 ecpay.com.tw 發起的跨站 POST：SameSite=Lax 的 gg_cart 不會帶。回傳端點不准讀、也不准發 cookie，只信自己發的選店票。
★ 回傳端點一律 303（不是 302），失敗也 303 帶 cvsSelectionError；form 用 ReadFormAsync 手動讀並接住解析例外，不要 [FromForm]。
★ 結帳解票放在 ExecuteIdempotentAsync 的 work 裡；指紋含選店票、不含解出來的名稱地址。選店票欄位不是 null 就一定要驗，不准退回去信 convenienceStoreCode。
★ 快取鍵用票的 SHA-256；票格式一個共用函式驗；時間經 IClock。
★ CompleteCheckoutRequest／CheckoutCompleted 只加 init 屬性，EventType 維持 v1；Cart.ReplayCompletedEvent 要帶新欄位。
★ migration 照 0018：SET ROLE greygray_owner → ALTER → RESET ROLE → owner 斷言（checkout、ordering）。表名是 ordering.orders。M1aCoreMigrationTests 第 106 行保持 0019。
★ Logistics:ECPay:MerchantId 空白＝沒有設定：開機照常、端點回 503／303 not-configured。有值時其餘設定開機就驗、壞了就炸。
★ 模擬器假地圖的 HTML 用可測純函式產生、全部 HtmlEncode、要有注入反例測試。start-dev-hosts 四個 Logistics__ECPay__* 全有／全無／部分就炸。
★ allow 在 Checkout／Ordering／Admin 縮到檔案層級；需要動清單外的檔就停下來問。
★ 後端樹 dev Host 與模擬器有在跑，不要起停；build 被 bin 鎖住就停下來回報。check-openapi.ps1 自己起停的短命 Host 允許。
★ 同一波前端樹有 FE-33 平行，不在你這棵樹，不用管它。
★ 派工書 §5 列了可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了——但契約 YAML 與 docs/05 不准動。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-33 的啟動 prompt（已撤包，保留供參考；在前端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js ＋ pnpm workspace）。這一包把結帳頁的超商門市從手動輸入代號改成 7-ELEVEN 電子地圖，並在前台與後台訂單詳情顯示門市名稱與地址。不動契約、不動 packages/ui。

GG_PACKAGE=FE-33

開工前務必先讀：
  CLAUDE.md                                前端四條 ＋ 派工規則
  frontend/README.md                       前端四條原文
  docs/45-開發工作流與設計準則.md            §5 自驗＝必要不充分、§6 邊界測試
  docs/00-decisions.md                     ADR-038（7-ELEVEN 電子地圖）
  docs/35-前端第二十一波派工書.md            ★ 整份讀完：§0 事實 ＋ §1 必做 ＋ §2 不要做 ＋ §3 所有權 ＋ §5 可能寫錯的地方
  docs/api/openapi.storefront.yaml         logistics 三條 operation、CheckoutRequest、Order（Leader 從後端樹複製來的，不准改）
  docs/api/openapi.admin.yaml              AdminOrder 三個門市欄位（不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-33.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。pnpm 指令都在 frontend/ 目錄裡跑：
     開工第一件事：pnpm api:generate（types.admin.ts 預期只多三個門市欄位，多了別的就停下來回報）
     交付前：pnpm --recursive typecheck、pnpm --recursive test（基準 558 ＋ 你新增的，逐專案條數貼進報告）
     在 repo 根：& 'C:\Program Files\Git\bin\bash.exe' .dispatch/audit-dispatch.sh
     輸出原樣貼進報告。build 由 Leader 跑，你不跑 next build，也不起、不停任何 dev server。

★ 進頁初始化照派工書必做 D 第 3 點的固定順序：讀草稿 → 解析回程參數 → 純函式合併 → 一次設定 state → 存回完整草稿 → 清網址 → 讀票。不要靠 effect 的執行順序。
★ 回程錯誤一律清掉選店票（就算草稿裡有舊門市）；404／422 清票用「只清選店票」的函式，不准 clearCheckoutDraft（會連留言一起刪）。
★ ConvenienceStoreField：頁面給 onStart(): Promise<CvsMapSession>（存草稿＋開票），元件只管同步 pendingRef 鎖、隱藏表單自動送出、失敗訊息、pageshow persisted 時放鎖。
★ 可否送出看「讀到了門市」，不是「有選店票 id」。送出只送 convenienceStoreSelectionId，不送 convenienceStoreCode。
★ useSearchParams 要 Suspense；PageTopBar 在外層，fallback 用同一組 Skeleton，不准 null。pageShell.test.ts 不改。
★ 不准手改生成檔；map session 的 body 型別從 paths 推導。mock 的既有超商訂單 fixture（前台與後台）要補門市欄位。
★ 錯誤訊息照派工書 §0.1 那張表一字不差。門市名稱地址不准放網址或 localStorage。
★ 同一波後端 BE-53 在另一棵樹實作，你的測試用 mock，不要打真後端。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了——但契約 YAML 與 docs/05 不准動。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## 上一波（第三十八波，已撤包）

第三十八波三包都已驗收撤包：後端 **BE-52**（`46ec1c5`：最愛清單後端，測試 332 → **340**）＋ 前端 **FE-31**／**FE-32**（`b7250db`：資訊頁、立即購買與最愛清單前端，測試 500 → **558**）。
**這一波還沒有部署**；部署時正式機要套 migration `0019`。真瀏覽器的畫面走查待補（Chrome 擴充未連線，這一次只做了 HTTP＋SSR 層的旅程）。

以下保留派工時的標頭與三份啟動 prompt 供參考。

**（派工時）第三十八波（2026-09-15）——三包平行、跨兩棵樹、`allow` 零重疊。**
後端 **BE-52**（最愛清單後端，`docs/51`）＋ 前端 **FE-31**（資訊頁，前端樹 `docs/34`）＋ 前端 **FE-32**（立即購買 ＋ 最愛前端，前端樹 `docs/34`）。
使用者以 `/goal` 下達：常見問題／關於我們／購買流程、最愛清單、7-11 取貨、立即購買，並要求「全體 ai-cli 一起處理，但不要把 WorkSpace 弄亂」。
**7-11 超商電子地圖是下一波**（要先做模擬器與物流憑證；研究結論見 `GreyGray_PM/00-進度總表.md`）。
契約（三條 `/v1/me/favorites`）、ADR-036／ADR-037 由 Leader 寫好並逐位元複製進前端樹。測試基準：後端 **332**、前端 **500**。
實作者分派（依 `~/.claude` 的 ai-cli 派工政策）：BE-52、FE-32 給 Codex `gpt-5.6-sol`＋high，FE-31 給 Claude Sonnet＋medium。
兩棵樹現在都**沒有** dev Host／dev server 在跑，子代理不要自己起。

---

## BE-52 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動 Catalog 模組、Storefront Host、一支新 migration（0019）、tests。契約 Leader 已寫好，不准改。沒有前端、沒有 ops/。

GG_PACKAGE=BE-52

開工前務必先讀：
  CLAUDE.md                                六條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md            §4 平行派工、§5 自驗＝必要不充分、§6 邊界測試六類
  docs/00-decisions.md                     ADR-036（最愛清單）、ADR-014（Contracts 之間要是 DAG）
  docs/51-後端第三十六波派工書.md            ★ 整份讀完：§0 事實（所有行號）＋ §1 必做 ＋ §2 不要做 ＋ §5 可能寫錯的地方
  docs/api/openapi.storefront.yaml         /v1/me/favorites 三條 operation（Leader 寫好的，不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-52.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     dotnet build .\GreyGray.slnx -c Debug --nologo（0 警告 0 錯誤；不要跑 ops\build.ps1，它會無條件跑整支測試）
     測試分專案前景跑，基準 332（含 2 個既有 Skip）＋ 你新增的；GreyGray.Architecture.Tests 必須在裡面、Total: 單獨貼出
     ops\check-openapi.ps1 -Configuration Debug
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ migration 照 0018 的結構：BEGIN → SET ROLE greygray_owner → 建表 → RESET ROLE → owner 斷言 → COMMIT。
  不 SET ROLE 的話 greygray_catalog 拿不到預設權限（0001 第 14-25 行的硬規則）。不准在 migration 裡 GRANT。
★ cursor 要同時帶 created_at 與 product_id——現有 Catalog 的 Slice／DecodeCursor 只放商品 id，不能沿用（#17／#21 同型）。
★ 重複／併發加入用 ON CONFLICT DO NOTHING，不准「先查有沒有、沒有再寫」。重複加入不改 created_at。
★ 可見規則＝商品詳情那一套（商品上架且至少一個上架 SKU），在 SQL 分頁之前過濾，不是撈出來再藏。
★ 匿名看商品時不准查最愛，測試要斷言「沒查」。
★ 資料庫層的測試一律用正式 migrations 建庫（M1aCoreMigrationTests 那一套），不准往 IdentityCatalog 手寫的 SchemaSql 加表。
★ check-openapi 的 M1a 模式只比 method＋path；參數、回應碼、204 要靠你自己的 HTTP 層測試。
★ 不准建指向其他 schema 的外鍵。
★ 同一波前端樹有 FE-31／FE-32 平行，不在你這棵樹，不用管它們。
★ 這台機器沒有 dev Host 在跑，不要手動起；check-openapi.ps1 自己起停的短命 Host 是允許的。
★ 派工書 §5 列了可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了——但契約 YAML 與 docs/05 不准動。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-31 的啟動 prompt（已撤包，保留供參考；在前端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js ＋ pnpm workspace）。這一包只做前台三個資訊頁（常見問題／購買流程／關於我們）與它們的入口。不動契約、不動 packages/*。

GG_PACKAGE=FE-31

開工前務必先讀：
  CLAUDE.md                                前端四條 ＋ 派工規則
  frontend/README.md                       前端四條原文（顏色尺寸只從 token、不做金額運算、不猜業務規則、不直接 fetch）
  docs/45-開發工作流與設計準則.md            §5 自驗＝必要不充分、§6 邊界測試
  docs/34-前端第二十波派工書.md              ★ 讀 §0（兩包共用）＋ §1（你的包）＋ §3 所有權 ＋ §5 可能寫錯的地方。§2 是 FE-32 的，只需知道它存在
  frontend/apps/storefront/app/_lib/__tests__/pageShell.test.ts   掃原始碼的測試寫法，你的入口／出口測試照這個精神
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-31.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。照派工書 §0.3：
     開發中只跑自己的測試檔：pnpm --filter storefront exec vitest run <你的測試路徑>
     交付前跑一次：pnpm --recursive typecheck、pnpm --recursive test（基準 500 ＋ 你新增的，逐專案條數貼進報告）
     bash .dispatch/audit-dispatch.sh
     輸出原樣貼進報告。build 由 Leader 跑，你不跑 next build，也不起、不停任何 dev server。

★ 不准自己補事實：客服電話、LINE、Email、地址、營業時間、出貨天數、付款期限的時數、退換貨規則、退貨運費、發票，全部沒有確認。
  「七天鑑賞期」也不准寫（ADR-025：代購適不適用由老闆判斷）。文案照派工書 §1.4；不要做放假資料或空欄位的「聯絡我們」。
★ 每一頁都要有入口與出口，用「掃原始碼」的測試釘住，掃到零個算失敗（#30／#32 兩次都是頁面存在但走不到）。
★ 「不准出現承諾」的測試是不存在型斷言：逐類注入一個反例確認它會紅，再還原，紅的輸出貼進報告。
★ metadata.title 只寫短標題（根 layout 的 template 會補「｜GreyGray」）；不要建 (info)/layout.tsx；寬度用 max-w-[var(--gg-container-max)]。
★ 不要改 tabs.ts／topBar.ts／_lib/__tests__/——新頁面不加規則就會有分頁列，那是對的；那幾個檔是 FE-32 的範圍。
★ 「我的」頁加的「我的最愛」連結指向 FE-32 同一波做的頁，現在點會 404 是預期的，不要做假頁。
★ 同一棵樹上 FE-32 平行在改 api-client 與商品頁。交付前那一次完整驗證若失敗在不屬於你 allow 的檔案：
  不重跑、不等待、不修，原樣寫進報告「我發現但沒做的事」。權威驗證由 Leader 在兩包都交付後序列跑。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了——但契約 YAML 與 docs/05 不准動。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-32 的啟動 prompt（已撤包，保留供參考；在前端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js ＋ pnpm workspace）。這一包做「立即購買」與「最愛清單」前端：api-client 重生型別與三支端點、商品頁兩顆按鈕、共用的最愛切換、/favorites 頁。不動契約、不動 packages/ui。

GG_PACKAGE=FE-32

開工前務必先讀：
  CLAUDE.md                                前端四條 ＋ 派工規則
  frontend/README.md                       前端四條原文
  docs/45-開發工作流與設計準則.md            §5 自驗＝必要不充分、§6 邊界測試
  docs/00-decisions.md                     ADR-036（最愛清單）、ADR-037（立即購買）
  docs/34-前端第二十波派工書.md              ★ 讀 §0（兩包共用）＋ §2（你的包）＋ §3 所有權 ＋ §5 可能寫錯的地方。§1 是 FE-31 的
  docs/api/openapi.storefront.yaml         /v1/me/favorites 三條 operation（Leader 從後端樹複製來的，不准改）
  .dispatch/reports/README.md              ★ 自驗報告格式

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-32.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。照派工書 §0.3：
     開工第一件事：pnpm api:generate（types.admin.ts 預期零 diff，有 diff 就停下來回報）
     開發中只跑自己的測試檔：pnpm --filter storefront exec vitest run <路徑>、pnpm --filter @greygray/api-client test
     交付前跑一次：pnpm --recursive typecheck、pnpm --recursive test（基準 500 ＋ 你新增的，逐專案條數貼進報告）
     bash .dispatch/audit-dispatch.sh
     輸出原樣貼進報告。build 由 Leader 跑，你不跑 next build，也不起、不停任何 dev server。

★ 連點與搶按：用同步 pendingRef 在任何 await 之前鎖住。setState('loading') 不是鎖，usePayloadIdempotency 也不防同時送兩個請求。
  「加入購物車」與「立即購買」共用一把鎖，第一次點擊的意圖勝出。
★ 最愛切換抽成一份共用邏輯：樂觀更新、失敗還原、同一件商品進行中不重送、401 先還原成未收藏再導去登入。不准卡片與商品頁各寫一份。
  卡片上不要求顯示「處理中」（ProductCard 傳不進去，不准改 packages/ui）。
★ ProductCardLink 的 priceFrom: null 分支也要有愛心、按了不導頁——收藏清單裡會出現未定價商品。
★ /favorites 自己做「載入更多」，不要改 InfiniteProductGrid；只有列表請求回 401 才導去登入，不要加 middleware。
★ SSR 快取已查證不會跨使用者外洩（派工書 §2.1），不准加任何快取設定。
★ 型別一律 pnpm api:generate，不准手寫請求型別。mock 沒有登入概念，mock 的 401 案例註明不適用即可。
★ cart 頁讀 from=buy-now：判斷寫成純函式（(checkout)/_lib/buyNowNotice.ts），頁面掛載後讀 window.location.search 交給它，不准用 useSearchParams()。
★ 不動 packages/ui、me/page.tsx（FE-31 的）、pageShell.test.ts、卡片的 <Link> 結構（FE-22 的裁決）。
★ 同一棵樹上 FE-31 平行在加資訊頁。交付前那一次完整驗證若失敗在不屬於你 allow 的檔案：
  不重跑、不等待、不修，原樣寫進報告「我發現但沒做的事」。權威驗證由 Leader 在兩包都交付後序列跑。
  後端 BE-52 在另一棵樹同時實作最愛端點；你的測試用 mock，不要打真後端。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了——但契約 YAML 與 docs/05 不准動。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## 上一波（第三十七波）

**（2026-09-06 下午）已撤包。** 第三十七波兩包都已驗收撤包：後端 **BE-49**（`2ad080a`：綠界五個值缺一個就開不了機，測試 315 → **332**）＋ 前端 **FE-30**（`398f37a`：出貨單與訂單在列表上互相看得見，測試 481 → **500**）。**這一波還沒有部署。**

★★ **部署這一波之前一定要先做**：正式機 `C:\Source\yc-deploy.ps1` 與 YC 上的 `secrets\ecpay.json` 改成五個值（`MerchantId`／`HashKey`／`HashIV`／`CheckoutUrl`／`CreditDetailUrl`），**否則部署後三個 .NET 服務會拒絕開機**——那是 BE-49 刻意造成的行為。

★ **BE-49 的子代理還沒動任何原始碼就停下來**，指出必做 E 自相矛盾：`ops/build.ps1:39` 無條件跑整支 `ops/test.ps1`，與「測試分專案前景跑」不可能同時成立。**這個矛盾從 BE-46 之後每一份派工書都帶著（BE-46／BE-47／BE-48 各一次），前面三包都照著跑、只是剛好沒炸。**下一波寫派工書時：建置寫 `dotnet build .\GreyGray.slnx -c Debug --nologo`，並要求把 `GreyGray.Architecture.Tests` 列進分專案清單、單獨貼出 `Total:`。

★ **行為變更**：`ops\start-dev-hosts.ps1` 不加 `-UseEcpaySimulator` 時，父行程必須提供五個 `Payment__ECPay__*` 值（以前三個就夠，網址吃預設）。

第三十七波這兩包分屬兩棵樹、`allow` 零重疊；派工時的基準是後端 `d49cf9b`、前端 `c78480d`，測試基準 後端 315／前端 481。上一波（第三十六波）BE-48 `89bb5c5`／FE-29 `b241f48` 也早已撤包並部署（release `20260904015905167`）。

★ 這一波之前 Leader 先補了 `docs/45-開發工作流與設計準則.md`（兩棵樹共用、**已加進 `audit-dispatch.sh` ⑦ 的比對清單**）＋ ADR-034（架構準則的界線）／ADR-035（跨平台手機）。閘門檔改過，所以稽核 **⑩ 會要求重跑 `selftest.sh`**（兩棵樹 × 兩個 agent，共四次）。
★ 使用者 2026-09-06 拍板 D3：測試資料**全部不留、全新資料庫重建**；**11 月中開張**，在那之前要開發完、測試完，而且使用者要自己做紅隊攻擊。計畫書 `docs/46-D3開張資料基準與還原演練計畫書.md`（後端樹）。

上一波（第三十六波）已撤包： 第三十六波兩包都已驗收撤包：後端 BE-48（`89bb5c5`：#44 結帳成功後讓購物車退休、#46 取消已出貨訂單不再卡住，測試 308 → 315）＋ 前端 FE-29（`b241f48`：#45 讓「掛了幾張出貨單、還差幾張沒簽收」看得見，測試 461 → 481）。第六次部署 release `20260904015905167` 一次過。
下一波派工前先讀 `ACTIVE.md` 的「已經通過」清單與本檔最後一節「下一波派工前」。

★ **BE-48**：正式機 log 有連續 15 次 `POST /v1/cart/checkout` → 422「購物車已完成結帳」——下單成功後 `gg_cart` 還指著已結案的車，全站只有「加入商品」那條路會換車。修：結帳成功就換新車、撞到已結案時換車並給自救指引、`GET /v1/cart` 拿到已結案的車也換（`CartView` 加 init 屬性且服務端要真的填值）。另修 BE-47 的回歸：取消已出貨的訂單時 `ReleaseByKeyAsync` 要對「已出庫」安靜跳過（那是退貨流程的事）。
★ **FE-29**：使用者說的「宅配到府不更新」不是配送方式的問題——那張訂單有三張出貨單、規則是全部簽收才轉已出貨，畫面卻沒講。修：訂單頁加出貨單區塊並說明還差幾張；建立出貨單時過濾掉已出貨／已完成／已取消，已有出貨單的標示但仍可勾。契約零改動。

第三十五波 BE-47（`ac63775`：出貨階段接起來，#43／#42）已驗收撤包，測試 286 → 308，第五次部署 release `20260903161959109` 含 migration `0018`；使用者走的兩張新單已驗證成立。

★ **BE-47**：使用者問「要不要再測一次完整流程」，Leader 先查正式機帳務 → **#43：出貨完全沒落帳也沒出庫**（`docs/02` 分錄表第 ⑦ 階段兩筆都沒人發沒人收；`inventory.lot` 還記著 60 件在倉庫、5 件永遠保留中）。修：Inventory 加「出庫」操作並訂閱 `ShipmentDispatched`（扣 on_hand＋reserved、reservation 轉已出庫、每筆 allocation 發 `StockCostAllocated`）；Ledger 補兩個 handler（DR 5100／CR 1300、DR 5200／CR 1100）；Ordering 品項轉 `Shipped`／`Completed`（#42）；加「分錄表每個階段都要有人發、有人收」的架構測試。

第三十四波 BE-46（`1d58dfa`：Worker 掛上 Fulfillment 模組——#41）已驗收撤包，第四次部署 release `20260903071555793` 後那則 `ShipmentDelivered` 事件處理完、訂單 ReadyToShip → Shipped。
第三十三波 BE-45（`485910d`：部署腳本）已撤包、YC 第三次真跑一次過。第三十二波 FE-28（`5b01314`，#40）已撤包並部署。第三十一波（BE-44 `fb15ad6`、FE-27 `56a221d`）已撤包並部署；**付款這條路 11:36 第一次在正式機用真的綠界測試站走通**。

★ **BE-46**：`Worker/Program.cs` 缺 `AddFulfillmentModule`（11 個模組、留了一句「之後再納入」），Ordering 的 `ShipmentDeliveredHandler` 對 `IFulfillmentQuery` 是 `Lazy` 相依 → 正式機 outbox `fulfillment.ShipmentDelivered.v1` 連炸 8 次 `ArgumentNullException`。修：模組清單抽成 `WorkerModules.AddWorkerModules`（加 Fulfillment）、Worker 開機驗每個 `IIntegrationEventHandler<T>` 與點名 `IFulfillmentQuery`、架構測試用同一個方法建容器＋空 orderIds 呼叫 `RecordShipmentDeliveredAsync` 重現炸點＋負向對照、Ordering 缺模組時的例外講人話。只動 Worker、Ordering.Core 一個方法、tests。

★ **BE-45**：`ops/lib/Deployment.ps1` 加 `Wait-ManagedServiceStart`（吃注入的 `-StartService`／`-GetStatus` scriptblock，成功條件只有 `Running`）與 `Suspend-WatchdogTask`／`Resume-WatchdogTask`；`deploy.ps1` START 迴圈改用它、`Invoke-Nssm start` 一律 `-AllowNonZeroExit`、STOP→健康檢查整段 try/finally 恢復 watchdog；self-test 第 19（AST ＋ 四案例）、20 項（假 scriptblock 四案例 ＋ AST 確認在 finally）。只動 `ops/` 三個檔。
★ **FE-28**：純函式 `subtotalPreview`（單價 × 數量，只用於顯示，ADR-033 唯一例外）＋ `BottomBarSummary`（「N 件 · 小計」）＋ `UnitPriceBlock`（資訊區單價）＋ `frontend/README.md` 例外註記；不動契約、`packages/*`、購物車／結帳頁。第三十波三段（BE-41、FE-26／BE-42、BE-43 七輪）已全部撤包；**正式機 YC 五個服務已上線（greygray.shop／admin.greygray.shop）**，使用者在後台建商品時撞到「沒有新增 SKU 的端點」，這一波就是補它。

★ **BE-44**：契約純新增一條 operation（Operator、Idempotency-Key、body `AdminSkuInput`、201 `AdminSku`、403／404／422，M1a）＋ `docs/05` 表加列 ＋ Admin Host `MapPost("/products/{productId}/skus")`（形狀照 PATCH `/skus/{skuId}`）＋ `OpenApiComponents` 清單 ＋ HTTP 層測試 ＋ `check-openapi` admin 30/30。
Catalog 模組的 `CreateSkuAsync` 早就在，不動模組。

★ **BE-43**：`ops/install-tunnel.ps1`（5.1 可跑、冪等：複製憑證到 `C:\GreyGray\cloudflared\`、寫 `config.yml` 四條 ingress ＋ 404、NSSM 登記 `GreyGray-Tunnel`）＋
`verify-environment.ps1` 多一項 ＋ `install-environment.ps1` **拿掉會動到現有 `cloudflared` 服務的那一段**（那是使用者其他應用共用的通道）。
tunnel `greygray`（`7daa50aa-…`）與兩筆 DNS Leader 已在 YC 上建好；`tunnel login`／`create`／`route dns` 不在腳本裡。

★ **FE-26**：跟上 ADR-030 的契約（`pnpm api:generate`、拿掉 `checkout/page.tsx` 的 `shippingPolicy!`）＋ #36 前端側（登出 `publishCart(null)`）
＋ FE-25 ⑦（`/login?next=` 亮 `next` 所屬分頁）＋ 結帳頁被帶去登入再回來保留已填內容（`sessionStorage`）。契約檔 Leader 已複製進前端樹，不要動。
★ **BE-42**：串真綠界的前置——`ReturnURL` 改成可由 `Storefront:PublicApiOrigin` 設定（通道／反向代理後面 `Request.Host` 是錯的）、
`deploy.ps1` 投遞兩個公開 origin、`build-frontends.ps1` 能指定另一棵樹的 `frontend/` 與各 app 的 API base。

★ **BE-41**：使用者親自走旅程第一張單就撞到 #37（純預購購物車結帳 500，比登入檢查還早）。問「哪一種是治本」後拍板「用第二種方式修」→
ADR-030：規則的主人是後端——契約 `shippingPolicy` 改成「混合才必填」（向下相容），後端混合沒帶回 422、單一模式依 line 組成推導。
併：壞 body 兩個環境都回 400 problem+json；#36 登出清 `gg_cart`＋「不是你的車」換新車；#38 兩支 dev 啟動腳本改 `Start-Process -Environment`。
★ dev Host 現在是 Release 在跑、使用者正在走旅程：子代理一律 `-Configuration Debug`，不准停 dev 行程。

---

## BE-49 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動 Payment 模組的組合根、ops/、tests。沒有契約變更、沒有 migration、沒有前端。

GG_PACKAGE=BE-49

開工前務必先讀：
  CLAUDE.md                          六條鐵則 ＋ 派工規則
  docs/45-開發工作流與設計準則.md      ★ 新增（兩棵樹共用）：設計準則、平行派工、自驗＝必要不充分、§6 邊界測試六類
  docs/47-後端第三十三波派工書.md      ★ 整份讀完：§0 事實（所有行號）＋ §1 必做 A～E ＋ §2 不要做 ＋ §5 可能寫錯的地方
  src/Modules/Payment/GreyGray.Modules.Payment.Infra/ModuleRegistration.cs   第 103-146（ReadEcpaySettings）、152-167（RequireEcpayEndpoint，不要改）、169-175（Required）
  ops/deploy.ps1                     第 229 附近（說明文字）、237-251（驗鍵）、266-269（注入）
  .dispatch/reports/README.md        ★ 自驗報告格式，以及「測試要分專案前景跑」

這一包要治的是一個「不會報錯的錯」：只填 MerchantId／HashKey／HashIV 三個值時，
CheckoutUrl 與 CreditDetailUrl 會 ?? 預設到 payment-stage.ecpay.com.tw，
而現有的網域守衛認得 *.ecpay.com.tw，所以測試站完全通過——正式金鑰會安靜地打到綠界測試站。

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-49.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤）
     測試分專案前景跑，基準 315 ＋ 你新增的，逐專案條數貼進報告
     ops\check-openapi.ps1 -Configuration Debug（這一包不該動契約，要維持綠燈）
     pwsh -File ops\self-test.ps1（改過 deploy.ps1 就要跑，BE-45 已有 20 項）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 不要改 RequireEcpayEndpoint（第 152-167 行）——它負責的是「正式機忘了拿掉 dev 模擬器設定」，做對了它該做的事。
★ 不要把正式站網址變成新的預設值，那只是把同一個病換一個方向。
★ 必做 A 會讓一票測試與 ops/ 腳本開不了機，那是預期的：用
  grep -rn "Payment:ECPay\|Payment__ECPay" src/ tests/ ops/
  掃過（目前 12 個檔）逐一補上兩個網址鍵，讓每個地方實際打到哪裡都不變——這一包只把「隱含」變成「明講」。
★ 模擬器那條路（ADR-029：AllowNonEcpayEndpoints=true ＋ 兩個網址指到模擬器）必須繼續可用，改完實際跑一次 EcpaySimulatorTests。
★ ops/ 的腳本要能在 PowerShell 5.1 跑（正式機只有 5.1），不要用 .NET 5+ 才有的 API。
★ 正式機的 C:\Source\yc-deploy.ps1 不在這棵樹裡，也不在這一包——那是 Leader 的事，不要試著連線。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-30 的啟動 prompt（已撤包，保留供參考；在前端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js ＋ pnpm workspace）。這一包只動 apps/admin 的 orders/ 與 shipments/ 兩個路由資料夾。不動契約、不動 packages/*、不動前台。

GG_PACKAGE=FE-30

開工前務必先讀：
  CLAUDE.md                          前端四條 ＋ 派工規則
  docs/45-開發工作流與設計準則.md      ★ 新增（兩棵樹共用）：§5 自驗＝必要不充分、§6 邊界測試六類
  docs/33-前端第十九波派工書.md        ★ 整份讀完：§0 事實（所有行號）＋ §1 必做 A～D ＋ §2 不要做 ＋ §5 可能寫錯的地方
  frontend/apps/admin/app/(dash)/shipments/_lib/orderShipments.ts   FE-29 已寫好的純函式（第 46／53／76／111 行），這一包重用、不要複製
  frontend/apps/admin/app/(dash)/shipments/[shipmentId]/page.tsx    第 46-49、70-76 行：limit:100 一次抓 ＋ 前端組 map ＋ 拿不到就退回原始 id 且不擋整頁
  .dispatch/reports/README.md        ★ 自驗報告格式

這一包補的是 FE-29 只做了一半的方向：訂單看得到它的出貨單，但
出貨單列表看不出掛的是哪一張訂單（使用者 2026-09-04 因此把交運按錯單），
訂單列表也看不出出貨進度。

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-30.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     pnpm --recursive typecheck（全綠）
     pnpm --recursive test（基準 481 ＋ 你新增的，逐專案條數貼進報告）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。build 由 Leader 跑，你不跑；不要起或停任何 dev server。

★ 不要複製 orderShipments.ts 的邏輯到 orders/ 底下，跨資料夾 import 現成那份；標籤用 shipments/_lib/labels.ts。
★ 訂單 map 查不到要退回 id.slice(0,8)，不是空白——查不到跟沒有掛訂單是兩件事。
★ 一張出貨單可以掛多張訂單，orderIds 是陣列。
★ 不要每一列各打一次 API（訂單列表有分頁，那是 N+1）：一次 limit:100 抓完前端比對，並在註解寫明這是刻意的取捨。
★ 不要顯示會騙人的總數（例如「共 N 張」），除非確定沒有下一頁。
★ 已取消的訂單不要顯示會誤導的出貨進度文字。
★ 邊界測試要涵蓋派工書 §1 必做 C 那五項（含「讀取失敗時兩張頁面都還能渲染其他欄位」）。
★ 派工書 §5 列了可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-48 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動 Storefront Host、Checkout 模組、Inventory 模組、tests。

GG_PACKAGE=BE-48

開工前務必先讀：
  CLAUDE.md                         六條鐵則 ＋ 派工規則
  docs/44-後端第三十二波派工書.md     ★ 整份讀完：§0 事實（正式機 log 連續 15 次 422 的實際紀錄、所有行號、CartView 沒有已結案欄位、#46 的回歸成因）＋ §1 必做 A～F ＋ §2 不要做 ＋ §5 可能寫錯的地方
  src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs   第 585-637（結帳）、985-996（加入商品換車，#36 的先例）、1049-1067（看購物車）、1073-1080（清 cookie）
  src/Modules/Inventory/GreyGray.Modules.Inventory.Infra/StockReservationService.cs   第 163 行：「已釋放就安靜成功」的先例，「已出庫」照這個形狀補
  .dispatch/reports/README.md       ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-48.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤；Leader 已把 dev Host 停掉，不會有檔案鎖）
     測試分專案前景跑，基準 308 ＋ 你新增的，逐專案條數貼進報告
     ops\check-openapi.ps1 -Configuration Debug（必做 C 動模組契約但不該動 HTTP 契約，要維持綠燈）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 換 cookie 放在真的成功之後；注意 ExecuteIdempotentAsync 的兩階段與重放（派工書 §5 第一條）。
★ 撞到「已結案」時換車但不要拿新車重試 checkout——新車是空的。
★ CartView 加欄位用 init 屬性、不動建構式，而且服務端要真的填值；只加屬性不填就是 #43 那種「型別有了沒人填」的形狀。
★ 不動釋放路徑的一致性守衛（是它把 #46 叫出來的）、不動契約 YAML、不新增 migration、不起停任何 Host、不碰正式機。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-29 的啟動 prompt（已撤包，保留供參考；在前端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js 15 App Router、pnpm workspace）。這一包只動 apps/admin 的訂單頁與出貨頁。

GG_PACKAGE=FE-29

開工前務必先讀：
  CLAUDE.md                         前端四條 ＋ 派工規則
  docs/32-前端第十八波派工書.md      ★ 整份讀完：§0 事實（那張訂單的三張出貨單、CreateShipmentDialog 沒有狀態過濾、訂單頁沒有出貨單資訊、為什麼不用改契約）＋ §1 必做 A～D ＋ §2 不要做 ＋ §5 可能寫錯的地方
  frontend/apps/admin/app/(dash)/shipments/[shipmentId]/page.tsx   第 46-49、70-73 行：listShipments/listOrders 抓一頁再在前端過濾的既有模式
  frontend/apps/admin/app/(dash)/shipments/_lib/labels.ts          出貨單狀態的標籤與 tone，直接共用不要重寫
  .dispatch/reports/README.md       ★ 自驗報告的格式

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-29.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     pnpm --recursive typecheck、pnpm --recursive test（基準 461 ＋ 你新增的，逐專案條數貼進報告）。
     build 由 Leader 跑，你不跑；不要起或停任何 dev server。

★ 已經有出貨單的訂單仍然可以勾（拆單合法），只要標示；不要直接拿掉。
★ 「還差幾張沒簽收」要從資料算出來，不要寫死；沒有出貨單時也要有話講，不要空白。
★ 不動契約、packages/*、apps/storefront，也不要求後端加 orderId 篩選（§0.4 說明過為什麼前端過濾就夠）。
★ 沒有 jsdom：判斷抽純函式測，文案用 renderToStaticMarkup（照 FE-28 的做法）。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-47 的啟動 prompt（已撤包，保留供參考；第二輪是 Leader 用同一個 session 補的裁決，見 `.dispatch/reports/BE-47.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動三個模組：Inventory、Ledger、Ordering，加 tests。

GG_PACKAGE=BE-47

開工前務必先讀：
  CLAUDE.md                         六條鐵則 ＋ 派工規則
  docs/43-後端第三十一波派工書.md     ★ 整份讀完：§0 事實（正式機帳務與庫存的實際數字、所有行號、現成可用的東西）＋ §1 必做 A～E ＋ §2 不要做 ＋ §5 可能寫錯的地方
  docs/02-事件與狀態機.md            第 161-186 行：M1 分錄對照表，這一包補的是第 ⑦ 階段兩筆
  src/Modules/Inventory/GreyGray.Modules.Inventory.Infra/StockReservationService.cs   第 18、163-200、428-435 行：釋放的完整形狀，出庫照它寫
  src/Modules/Ledger/GreyGray.Modules.Ledger.Infra/LedgerEventHandlers.cs             第 13-59 行：LedgerPostingService 的用法樣板
  tests/GreyGray.Architecture.Tests/WorkerCompositionTests.cs                          BuildConfiguration() 的 in-memory 設定樣板
  .dispatch/reports/README.md       ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-47.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤）
     測試分專案前景跑（整支 ops\test.ps1 會超過 600 秒被移到背景然後整輪消失），基準 286 ＋ 你新增的，逐專案條數貼進報告
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 觸發點是「交運」（ShipmentDispatched）不是「簽收」；不要動 ShipmentDelivered 那條路（BE-46 剛修好）。
★ 一張出貨單可合併多張訂單、一張訂單可拆多張出貨單：冪等要靠 reservation 狀態自己擋，框架的 processed_message 擋不到。
★ 不新增 migration；quantity_available 是 generated column 不准寫；金額一律 Money。
★ 不碰正式機、不動 Fulfillment 模組、不動契約 YAML、不動前端與 ops/、不起停任何 Host。
★ 派工書 §5 列了六個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-46 的啟動 prompt（已撤包，保留供參考；第二輪是 Leader 用同一個 session 補的裁決，見 `.dispatch/reports/BE-46.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包只動三處：Worker host 的組合根（src/Hosts/GreyGray.Worker/）、Ordering.Core 一個方法的錯誤訊息、tests/。

GG_PACKAGE=BE-46

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/42-後端第三十波派工書.md      ★ 整份讀完：§0 事實（正式機 outbox 失敗證據、Worker/Program.cs 行號、Ordering 的 Lazy 相依、為什麼測試沒抓到、不用資料庫的重現法）＋ §1 必做 A～E ＋ §2 不要做 ＋ §5 可能寫錯的地方
  src/Hosts/GreyGray.Worker/Program.cs                         第 59-83 行：模組清單與開機驗證區塊
  src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/ModuleRegistration.cs   第 76-86 行：Lazy<IFulfillmentQuery?> 的由來（不動）
  tests/GreyGray.Architecture.Tests/ModuleCompositionRootTests.cs   第 79-90 行：in-memory configuration 的樣板
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-46.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤）
     ops\test.ps1 -Configuration Debug（基準 284 ＋ 你新增的，逐專案條數貼進報告）
     dev 環境真的把 Worker 起一次（必做 B-4；用新 shell、看 log；只起你自己那個行程、用完停掉）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 模組清單只有一份：WorkerModules.AddWorkerModules；Program.cs 與架構測試都呼叫它。
★ 開機驗證要點名 IFulfillmentQuery（Lazy 相依光解析 handler 抓不到）；負向對照那條測試要真的 throw。
★ 不動 Admin／Storefront 的模組清單、不動 Ordering.Infra 的 Lazy、不動 Fulfillment、不動契約、不動 ops/、不碰正式機。
★ 一律 -Configuration Debug；dev 的 Host 不准停。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-45 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）——但這一包只動 ops/ 底下三支 PowerShell：deploy.ps1、lib/Deployment.ps1、self-test.ps1。正式機 YC 是 Windows PowerShell 5.1，沒有 pwsh。

GG_PACKAGE=BE-45

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/41-後端第二十九波派工書.md    ★ 整份讀完：§0 事實（13:02 正式機怎麼死的、deploy.ps1 行號、YC 量到的 nssm 行為、watchdog 空窗）＋ §1 必做 A～D ＋ §2 不要做 ＋ §5 可能寫錯的地方
  ops/self-test.ps1                第 268-330 行：AST 把關與最小重現的樣板（新兩項照這個寫）
  ops/lib/Deployment.ps1           Clear-NssmAppParameters：注入 scriptblock 的樣板
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-45.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\self-test.ps1   （5.1，20/20）
     pwsh -NoProfile -File ops\self-test.ps1                                      （7，20/20）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。不跑 build.ps1、不起任何 Host、不碰 YC。

★ 成功條件只有 SCM 狀態 Running：nssm start 的 exit code 對「正在起」與「已在跑」都回非 0，不能當訊號；不准固定 sleep、不准字串比對當成功。
★ Resume-WatchdogTask 一定在 finally（失敗的部署也要把 watchdog 還回去）。
★ 不准 GetNewClosure()（self-test 第 18 項會擋）、不准 .NET Core 專屬 API；self-test 不准呼叫真的 *-ScheduledTask。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-28 的啟動 prompt（已撤包，保留供參考；第二輪是 Leader 用同一個 session 補的裁決，見 `.dispatch/reports/FE-28.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js 15 App Router、pnpm workspace：apps/storefront、apps/admin、packages/api-client、packages/ui）。這一包只動前台 apps/storefront 的商品詳情頁。

GG_PACKAGE=FE-28

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/31-前端第十七波派工書.md      ★ 整份讀完：§0 事實（AddToCartPanel／page.tsx 行號、PriceDisplay、規則原文、測試基礎）＋ §1 必做 A～E ＋ §2 不要做 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-033（前端「不做金額運算」的唯一例外：商品頁小計預覽）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-28.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：pnpm --recursive typecheck、pnpm --recursive test（基準 436 ＋ 你新增的，逐專案條數貼進報告）。build 由 Leader 停 dev server 後跑，你不跑。

★ 小計只准商品頁用（subtotalPreview 全 repo 一個呼叫端）；購物車／結帳／訂單頁的金額仍全部來自後端，一個字不動。
★ 不動契約、packages/ui、packages/api-client、admin app；page.tsx 的 SSR 內容不動（單價放 AddToCartPanel 頂端）。
★ 沒有 jsdom：「5 件 → NT$300」靠純函式 ＋ 純呈現元件（BottomBarSummary／UnitPriceBlock）用 renderToStaticMarkup 測。
★ dev server 由 Leader 管：不停、不起、不跑 next build。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-27 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js 15 App Router、pnpm workspace：apps/storefront、apps/admin、packages/api-client、packages/ui）。這一包只動後台 app 與 api-client。

GG_PACKAGE=FE-27

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/30-前端第十六波派工書.md      ★ 整份讀完：§0 事實（商品頁、SkuEditDrawer、api-client 現況、契約新舊端點）＋ §1 必做 A～D ＋ §2 不要做 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-032（修訂凍結契約：純新增 POST /v1/products/{productId}/skus；後台補新增 SKU 與批號進貨）
  docs/api/openapi.admin.yaml      ★ Leader 從後端樹複製來的，不要動；/v1/products/{productId}/skus（新）與 /v1/lots（M2 既有）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-27.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：pnpm api:generate（貼 diff --stat：types.admin.ts 有、types.storefront.ts 零 diff）、
     pnpm --recursive typecheck、pnpm --recursive test（基準 410 ＋ 你新增的，逐專案條數貼進報告）。build 由 Leader 停 dev server 後跑，你不跑。

★ 型別一律從重生的 types.admin.ts 拿（S['AdminSkuInput']、S['AdminSku']、S['Lot']、paths['/v1/lots']['post'] 推導 body）；不准手寫請求型別。
★ 不改 docs/api/*.yaml、docs/05；不動 storefront app；不加側邊欄新頁面（進貨做在商品頁 SKU 列的抽屜）；不在前端算庫存或金額。
★ NT$ → amountMinor 的轉換照 SkuEditDrawer 現貨標價既有的 helper；冪等鍵照既有 createProduct／updateSku 呼叫端的 MutationOptions 做法。
★ dev server 由 Leader 管：不停、不起、不跑 next build；需要活體才能驗的事寫進報告交給 Leader。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-44 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-44

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/40-後端第二十八波派工書.md    ★ 整份讀完：§0 事實（契約現況、模組 port、Host 樣板、OpenAPI 元件清單）＋ §1 四個必做 ＋ §2 不要做的事 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-032（修訂凍結契約：純新增 POST /v1/products/{productId}/skus）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-44.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：dotnet build -c Debug（0 警告 0 錯誤）、
     ops\test.ps1 -Configuration Debug（12 個專案全過；整套跑不動就前景逐一跑）、
     ops\check-openapi.ps1 -Configuration Debug（admin 從 29/29 變 30/30）、pwsh ops\self-test.ps1 exit 0。輸出貼進報告。

★ 一律 -Configuration Debug；不碰 Release bin（那是部署用的）。dev Host 目前沒在跑，不要自己起 Host。
★ 契約只准純新增一條 operation（派工書 §1 必做 1 的 YAML 逐字）；AdminSkuInput／AdminSku／AdminProductInput 一個字不動；docs/05 那張表加一列標 M1a。
★ Catalog 模組不動（CreateSkuAsync 早就在）；不動前端、ops/、db/、docs/00-decisions.md。
★ 404／422 的映射照既有 BffHttp.StatusFor；不改 BffHttp。不捏造預設重量／尺寸。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-43 的啟動 prompt（已撤包，保留供參考；第三～七輪是 Leader 用同一個 session 補的指示，見 `.dispatch/reports/BE-43.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。這一包只有 ops 腳本與文件，不碰 C#。

GG_PACKAGE=BE-43

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/39-後端第二十七波派工書.md    ★ 整份讀完：§0 事實（正式機現況、cloudflared 本機管理做法）＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-031（拓樸與主機名稱：greygray.shop 根網域、admin.greygray.shop）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-43.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。這一包沒有 C# 改動，不必跑 12 個測試專案；
     要跑的是：三支腳本 Parser::ParseFile（5.1 與 7 各一次）、
     powershell.exe -NoProfile -File ops\environment-self-test.ps1 OVERALL PASS、
     pwsh -NoProfile -File ops\self-test.ps1 exit 0（它會斷言正式機腳本有 BOM——install-tunnel.ps1 是正式機腳本，要有 UTF-8 BOM）。

★ 正式機只有 Windows PowerShell 5.1：不用 ??、?.、三元運算子、Start-Process -Environment、#Requires -Version 7。
★ 絕對不碰現有的 Cloudflared 服務、C:\ProgramData\cloudflared\token、CloudflaredWatchdog（使用者其他應用共用）。
★ install-tunnel.ps1 不做 tunnel login／create／route dns（Leader 手動，已完成：tunnel greygray、兩筆 CNAME）。
★ 不在開發機真的登記服務或跑 cloudflared；-ValidateOnly 印出將寫入的 config.yml。開發機沒裝 cloudflared——
  ingress validate 那一步在報告裡寫「開發機無 cloudflared，留給 Leader 在 YC 跑」，不要自己 winget install。
★ 不要動 deploy.ps1、src/、docs/api/、docs/00-decisions.md。

★ BOM：install-tunnel.ps1 要有 UTF-8 BOM（正式機腳本）；其他檔維持原本狀態。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-26 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。pnpm monorepo：apps/storefront、apps/admin（Next.js App Router）、packages/api-client（openapi-typescript 產生型別）。

GG_PACKAGE=FE-26

開工前務必先讀：
  CLAUDE.md                         四條鐵則 ＋ 派工規則
  docs/29-前端第十五波派工書.md      ★ 整份讀完：§0 事實 ＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md              ADR-030（規則的主人是後端，前端不補預設值）
  .dispatch/reports/README.md       ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-26.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。pnpm --recursive typecheck 與 pnpm --recursive test
     在前景跑完、貼原文（基準 376 條，總數只增不減）。
     ★ 前台／後台 dev server 正在跑（5002／5003）會 hot reload：不要停它們、不要跑 next build。

★ 必做 A：pnpm api:generate 重生型別；拿掉 checkout/page.tsx 第 152 行的 shippingPolicy!；
  types.admin.ts 預期零 diff，有 diff 就停下來回報。cartRules.ts 的判斷不動、不補預設值。
★ 必做 B：登出成功後 publishCart(null)。
★ 必做 C：/login、/register 帶 ?next= 時分頁列亮 next 所屬的分頁（用 safeNext），沒帶就亮「我的」；
  tabs.test.ts 既有斷言一條都不刪。
★ 必做 D：結帳頁 401 導向登入前把五個欄位存 sessionStorage（鍵含 cart id），回來時還原、送出成功就刪；
  純函式＋測試；不用 localStorage、不存整份購物車。

★ 不要改 docs/api/*.yaml、docs/05（Leader 從後端樹複製來的）、.env.local、packages/api-client 手寫的部分。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-42 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-42

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/38-後端第二十六波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-031（拓樸與主機名稱是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-42.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 三個 Host ＋ 模擬器用 Release 跑著、使用者隨時會用：建置與測試一律
       -Configuration Debug，不要停掉或重啟任何 dev 行程，不要碰 D:\GreyGray\。

★ 必做 1：ReturnURL 抽成 BuildEcpayReturnUrl(configuration, request)——Storefront:PublicApiOrigin
  有設就用它（絕對 http(s)、去結尾斜線、壞值丟例外含鍵名），沒設維持 request 的 scheme/host。
  純函式測試放 tests/GreyGray.M1a.CheckoutOrdering.Tests/。start-dev-hosts.ps1 加選填 -StorefrontPublicApiOrigin。
★ 必做 2：deploy.ps1 加 Mandatory 的 -StorefrontPublicOrigin／-StorefrontPublicApiOrigin，只注給
  GreyGray-Storefront；-ValidateOnly 也驗；正式機是 Windows PowerShell 5.1，不准用 7 的語法。
★ 必做 3：build-frontends.ps1 加 -FrontendRoot／-StorefrontApiBaseUrl／-AdminApiBaseUrl，兩個 app 各自建、
  各自帶 NEXT_PUBLIC_API_BASE_URL 與 NEXT_PUBLIC_USE_MOCK=0，建完 grep artifact 確認吃到值且沒有 127.0.0.1；
  環境變數用完 Remove-Item Env:（不要用 $null 還原）。build.ps1 -Publish 傳下去。
★ 必做 4：docs/14 加「部署五個 app 服務」與「開發機怎麼產 artifact」。

★ 不要在任何一棵樹真的跑 next build；不要碰 cloudflared／通道；不要動 src/Modules/、src/Platform/、
  docs/api/、docs/05、docs/00-decisions.md、frontend/、install-*.ps1、stop-dev-environment.ps1、verify-environment.ps1。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-41 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-41

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/37-後端第二十五波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 五個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-030（這一包的形狀是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-41.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 三個 Host 現在用 Release 跑著、使用者正在上面走旅程：建置與測試一律
       -Configuration Debug，不要停掉或重啟任何 dev 行程，不要碰 D:\GreyGray\。

★ 形狀是拍板過的（ADR-030）：規則的主人是後端。契約 shippingPolicy 改成
  「Cart.hasMixedModes 為 true 才必填，否則可省略或 null」；後端混合沒帶 → 422
  checkout.shipping-policy-required；單一模式忽略客人的值、依 line 組成推導
  （純現貨 ShipSeparately、純預購 HoldUntilComplete）。
  CheckoutCompleted.ShippingPolicy 與 Order.ShippingPolicy 維持不可為 null，
  Ordering 一行不動、不做 migration。

★ 壞掉的 request body：Development 與 Production 都要 400 + application/problem+json
  （platform.malformed-request），證明要走真管線（tests/GreyGray.EndToEnd.Tests 的 StartHost 模式）。

★ #36 後端側：/auth/logout 同時刪 gg_cart；GET /v1/cart 與 POST /v1/cart/lines 拿到
  checkout.cart-not-found 且帶著 cookie 就換新車（服務層的 not-found 語意不要改）。

★ #38：ops/start-dev-hosts.ps1 與 ops/start-dev-ecpay-simulator.ps1 改 Start-Process -Environment，
  刪掉改父行程再還原那一段，檔頭加 #Requires -Version 7.4。證明用用完即丟的腳本，不進 repo。

★ 不要改前端 worktree、src/Modules/Ordering/、src/Modules/Payment/、src/Platform/、
  Directory.Packages.props、ops/deploy.ps1、install-dev-environment.ps1、stop-dev-environment.ps1。
★ 不要新開測試專案。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## 上一波（第二十九波，已撤包）的啟動 prompt 保留在下面供參考格式

★ **BE-40**：使用者拍板「先做 dev 模擬付款，但要能隨時換回 adapter」。Leader 的裁決（ADR-029）是
**假的是綠界的伺服器**——獨立行程的模擬器，`EcpayGateway` 與回呼判斷一個位元組不動，dev 只把
`Payment:ECPay:CheckoutUrl`／`CreditDetailUrl` 指過去；正式碼唯一新增 `AllowNonEcpayEndpoints` 守衛。
派工前查證出真缺陷 **#33**（簽章沒有 `ClientBackURL`，付完款沒有路回商店），併入。

★ **FE-25**：「我的」一直沒有家（分頁與首頁頭像都指 `/orders`、`/wallet` 零入口、全站沒有登出），
登入後一律被丟到 `/orders`、結帳送出撞 401 沒有去登入的路。做 `/me`、`?next=` 回跳、付款結果頁有限次重查。
兩個追蹤項 Leader 查證後不用改碼（`GET /v1/cart` 不寫 DB；cookie HttpOnly 前端讀不到）。

★ 2026-09-02 使用者從前台測整段下單時撞到 **#30**：加完購物車之後沒有任何按鈕
回得去，只能按上一頁；首頁上連「購物車」三個字都沒有。根因是當初就沒排——
八包裡只有 FE-6「後台：殼」，**前台從來沒有這一包**。

★ **2026-09-02 使用者第二次撞到同一件事（#32）**：FE-23 撤包後接著測，問「昨天提到的
加入購物車後沒有按鈕可以返回首頁，這個沒優化嗎？」——答案是**沒有**。FE-23 修的是有分頁列
的頁面；使用者撞到的三頁（商品詳情、購物車、結帳）正好是分頁列刻意隱藏的三頁，一頁都沒修到。
Leader 的錯：只寫了「不要顯示」，沒給替代出口。**已由 FE-24 修掉（`41e9fd7`），子代理五個自主判斷全對。**

已通過整合驗收並撤包：

| 包 | 內容 | commit |
|---|---|---|
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |
| BE-37 | 冪等錯誤碼對齊契約（#24） | `f8e357f` |
| BE-38 | M2 批發進貨與批號列表，前台第一次有東西可以買 | `f191120` |
| FE-23 | 前台的殼：底部分頁列 ＋ 購物車徽章（#30），測試 150 → 248 條 | `5b2db68` |
| FE-24 | 三頁的頂部列 ＋ 提示加「查看購物車」＋ 修 #31（#32），測試 248 → 317 條 | `41e9fd7` |

★ **2026-09-01 真 Chrome 全站逐頁複驗查出四個新缺陷**（詳見 `GreyGray_PM/00-進度總表.md`）：

| # | 內容 | 在哪棵樹 |
|---|---|---|
| ~~#29~~ | ~~後台首頁的財務數字是寫死的假資料~~ | ✅ **FE-21 已修**（`ebe074c`），測試 135 → 146 條 |
| ~~#26~~ | ~~預購商品從商品路徑永遠買不到~~ | ✅ **BE-39 已修**（`d43dc1d`），測試 227 → 241 條 |
| ~~#27~~ | ~~商品卡收藏心點了會跳到商品頁~~ | ✅ **FE-22 已修**（`6f3f380`） |
| ~~#28~~ | ~~前台必填欄位只有視覺上的 `*`~~ | ✅ **FE-22 已修**（同上），前端測試 135 → 150 條 |

★ **2026-09-01 真 Chrome 複驗查出的四個缺陷已全數修正並驗收撤包。**
剩下的 **#25 仍未解**（卡 E3），是唯一還擋著「一條完整流程走得完」的技術缺陷。

★ **#25 仍未解**：任何會解析 Payment 模組的端點都在 DI 階段炸掉——
storefront `GET /v1/cart`、`POST /v1/cart/lines`，**以及 admin `GET /v1/orders/{orderId}`**
（後台訂單列表點得進去、點開任一張就 500，已在真瀏覽器裡驗證）。卡在 E3。

**這一波的派工書：後端 `docs/36-後端第二十四波派工書.md`（BE-40）、前端 `docs/28-前端第十四波派工書.md`（FE-25）。
下一份後端派工書是 `docs/37-後端第二十五波派工書.md`；下一份前端派工書是 `docs/29-前端第十五波派工書.md`。**

★ **`pnpm lint` 在這個 workspace 根本跑不起來**（沒裝 ESLint，`next lint` 已棄用且互動式；
對沒碰過的專案跑也是 exit 1，Leader 已用對照組確認）。**不要再把它列進任何自驗項。**

---

## BE-40 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-40

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/36-後端第二十四波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 五個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-029（這一包的形狀是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-40.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 環境開著時 Host 會鎖住 bin\Debug，建置與測試都用 -Configuration Release，
       不要停掉或重啟任何 dev 行程。

★ 形狀是拍板過的：假的是綠界的伺服器（獨立行程的模擬器），不是我們的 adapter。
  EcpayGateway 與回呼判斷一個位元組都不動；正式碼唯一新增的是
  Payment:ECPay:AllowNonEcpayEndpoints 守衛（預設 false）。

★ #33 併入：簽章加 ClientBackURL，由 Host 用 Storefront:PublicOrigin 組出
  {PublicOrigin}/payment/result?orderId=…。缺設定在付款端點明確炸，不要預設 localhost。
  不做 OrderResultURL。

★ 不要動 AllowSimulatedPaid、不要讓模擬器送 SimulatePaid=1。
★ 不要改 docs/api/*.yaml、docs/05、frontend/、Directory.Packages.props、ops/deploy.ps1。
★ 不要新開測試專案；測試放 tests/GreyGray.M1a.PaymentLedger.Tests/。
★ 模擬器只 ProjectReference Payment.Infra，簽章走 InternalsVisibleTo 重用，不要再抄一份。
★ 模擬器的 MerchantId 不是 DEVFAKE 開頭就拒絕啟動；TradeNo 以 DEVFAKE 開頭、共 20 字。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。

檔案所有權：見派工書 §4。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-25 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-25

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/28-前端第十四波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 A～E
  .dispatch/reports/FE-24.md       上一包的報告，尤其「我發現但沒做的事」④⑦
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-25.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 三件事：A 新頁 /me（含登出）＋ B「我的」改指 /me ＋ C 登入後回到原頁（?next=，只收站內路徑）
  ＋ D 付款結果頁有限次自動重查。E 兩個追蹤項已由 Leader 查證為不成立／前端做不到，不改碼，只寫進報告。

★ tabs.test.ts 那兩段釘住「我的 → /orders」的斷言允許改（這一包就是要改它），總條數只能增不能減。
  基準 317 條，交付時必須變多。

★ 不要跑 next build——dev server 在跑，共用 .next，會把整站打成 500。typecheck 與 vitest 就夠。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml 或任何 package.json。測試放 apps/storefront。pnpm lint 跑不起來，不要列。

★ 不要動後端、docs/api/*.yaml、packages/*、globals.css、TAB_BAR_RULES、TOP_BAR_RULES、
  useCartItemCount.ts、StorefrontTabBar.tsx、PageTopBar.tsx。

★ 環境提示：dev 全開（前台 5002、後台 5003、API 5000／5001）。不要重啟或停掉任何 dev server。
  後端同時有另一包（BE-40）在另一棵樹做付款模擬器，跟你無關，不要等它。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。這跟「不准自己擴大範圍」是兩件事。

你不可以自己宣告通過。交付完就停。
```

---

## FE-24 的啟動 prompt（已撤包，保留供參考）

三頁的頂部列（商品詳情 · 購物車 · 結帳）＋ 提示加「查看購物車」＋ 順手修 #31。
修「現在卡在哪」**#32**：FE-23 撤包後使用者接著測，問「昨天提到的加入購物車後沒有按鈕
可以返回首頁，這個沒優化嗎？」——答案是沒有。FE-23 修的是有分頁列的頁面，
使用者撞到的三頁正好是分頁列刻意隱藏的三頁。Leader 的錯：只寫了「不要顯示」，沒給替代出口。

★ 派工前已先 commit 閘門檔（這次有照做），子代理收工沒有再撞 stop gate。
★ 2026-09-02 已驗收撤包（`41e9fd7`）。子代理五個自主判斷全對（詳見 `.dispatch/ACTIVE.md` 的撤包註記與
  `.dispatch/reports/FE-24.md`「我發現但沒做的事」）；build 由 Leader 停掉 dev server 後跑，EXIT=0。

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-24

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/27-前端第十三波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 要做什麼
  .dispatch/reports/FE-23.md       上一包的報告，尤其「我發現但沒做的事」①⑤
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-24.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 使用者撞到的是：商品詳情、購物車、結帳這三頁沒有任何出口。修法是使用者拍板的
  「這三頁加一條頂部列」（左返回、中頁名、商品頁右邊帶徽章的購物車圖示），見 §1。

★ 最重要的一條結果：每一條路由恰好有一種殼（分頁列或頂部列），要用原始碼掃描的
  測試釘住（照 bottomActionBarCollision.test.ts 的做法），不要再抄一份清單。

★ 返回在直接打網址進來時也要有地方去（商品→/、購物車→/、結帳→/cart），用測試釘住。

★ 不要動 globals.css:88 那行 body 留白——上一包 Leader 寫錯、子代理擋下來的 144px 教訓。

★ 不要跑 next build——dev server 在跑，共用 .next，會把整站打成 500。typecheck 與 vitest 就夠。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml 或任何 package.json。測試放 apps/storefront（有 vitest），
  packages/ui 沒有，寫在那裡永遠不會被執行。基準 248 條，交付時條數必須變多。

★ pnpm lint 在這個 workspace 根本跑不起來（沒裝 ESLint），不要列進自驗、也不要假裝通過。

★ Toast 後台也在用，新的動作 prop 必須是選填、不改既有畫面。

★ 不要動後端、不要動 docs/api/*.yaml、不要做全站頂部頁首。

★ 環境提示：dev 全開（前台 5002、後台 5003、API 5000／5001）。
  Host 是用 D:\GreyGray\start-dev-hosts-with-fake-ecpay.ps1 起的（注入了開發用假綠界設定）。
  不要重啟或停掉任何 dev server。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。
  上一包就是這樣擋下 Leader 寫錯的一段，而且是對的。這跟「不准自己擴大範圍」是兩件事。

你不可以自己宣告通過。交付完就停。
```

---

## FE-23 的啟動 prompt（已撤包，保留供參考）

補「前台的殼」——底部分頁列，修「現在卡在哪」**#30**。
使用者 2026-09-02 從前台測整段下單時撞到：加完購物車之後沒有任何按鈕回得去，
只能按上一頁；首頁上連「購物車」三個字都沒有。
根因是當初就沒排：八包裡只有 FE-6「後台：殼」，前台從來沒有這一包。

```
GG_PACKAGE=FE-23

專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/26-前端第十二波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 要做什麼
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

你要做的事：補「前台的殼」——底部分頁列（首頁 · 開團 · 購物車 · 我的），
購物車帶數量徽章。

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-23.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 最容易做錯的是與既有固定底部列打架。BottomActionBar 是 fixed bottom-0、高 72px，
  已經有三頁在用：商品詳情的 AddToCartPanel、購物車頁、結帳頁。
  那三頁不要顯示分頁列，而且要有測試釘住這份清單。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml。把判斷抽成純函式來測。
  測試要放在 apps/storefront（有 vitest），packages/ui 沒有，寫在那裡永遠不會被執行。

★ pnpm lint 在這個 workspace 根本跑不起來，不要列進自驗、也不要假裝通過。

★ 徽章拿不到資料時不要顯示 0——「0 件」與「不知道幾件」是兩件事。

★ 加入購物車成功後徽章要立刻更新，不能等重新整理。

★ 不要動後端、不要動 docs/api/*.yaml、不要做頂部頁首。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。

你不可以自己宣告通過。交付完就停。
```

### ★ 這一包的兩個裁決與一次「Leader 寫錯被子代理擋下」

1. **派工書 §1「要在 layout 加底部留白」是錯的**——`globals.css:88` 早就在 `<body>` 上
   放了同一個算式，照字面加會變成 144px。子代理查證後否決並換了做法（分頁列高度
   寫成與那一行逐字相同的算式，用測試讀 `globals.css` 比對釘住）。
   Leader 在真瀏覽器複驗：`body` padding-bottom 實測 **72px**，沒有加倍。**子代理是對的。**
2. **徽章取數量總和，不是品項數**（Leader 裁決）。決定性理由是子代理自己寫的可及名稱
   唸「購物車，X 件」——一行 quantity=5 唸成「1 件」是錯的。而且後端會把同一個 SKU
   併進同一行，取 `lines.length` 時「再加一次」徽章不會動，等於「按了沒反應」。
3. **順手要求檢查的 NaN 風險是真的**：裸 `reduce` 在 quantity 是 `undefined` 時產出 `NaN`、
   是字串 `'3'` 時字串串接成 `'03'`、是 `null` 時靜靜少算。已擋掉並補測試。

★ **這一包 Leader 沒有照第 316 行「派工前先 commit 閘門檔」做**，
結果子代理每一輪收工都被 stop gate 要求還原 `.dispatch/ACTIVE.md`（＝它自己的授權）。
它兩次都正確拒絕並舉證。**下次一定要先 commit。**

---

## BE-39 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-39

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/35-後端第二十三波派工書.md    §0 事實 ＋ §1 要補什麼與那條規則
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-39.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 環境開著時 Host 會鎖住 bin\Debug，用 -Configuration Release 繞開，
       不要去停掉任何 dev server。

★ 契約是對的、程式沒跟上——不要改 docs/api/*.yaml，不要動前端
  （AddToCartPanel 已照契約寫好，修好後端前端零行變更就會動）。

★ Sku.available 預購恆 0 是契約明文，不要改那段邏輯。

★ §1 那條「同一個 SKU 掛多個開著的團取哪一個」的規則要有專屬測試釘住；
  若發現規則與既有假設衝突，停下來寫進報告問，不要自己換一條。

★ 補測試釘住 campaignId／campaign／price／campaignOfferId 這四個欄位——
  #26 能活到現在正是因為全 repo 沒有任何測試斷言過它們。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-22 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-22

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/25-前端第十一波派工書.md      §0（#27）＋ §1（#28）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-22.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。

★ 這個 workspace 沒有 jsdom／@testing-library，也沒有安裝（Leader 已查證）。
  不要為了寫測試去加相依套件、不要動 pnpm-lock.yaml。
  #27 的迴歸保證改用「把 handler 抽成可單元測試的小函式」，見派工書 §0。

★ pnpm lint 在這個 workspace 根本跑不起來（沒裝 ESLint），不要列進自驗、
  也不要假裝通過。測試用 pnpm --recursive test（基準 146 條）。

★ #27 不要把 ProductCard 搬出 <Link>、不要改 ProductCardLink 的結構。

★ #28 只補標了 * 的欄位；register-email 與 register-referral-code 是選填，
  不要加 required。不改表單的送出行為——若補了 required 之後瀏覽器開始擋送出、
  使既有錯誤訊息路徑走不到，停下來寫進報告問。

★ 不要動後端、不要動 docs/api/*.yaml。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-21 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-21

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/24-前端第十波派工書.md        §0 事實 ＋ §1 照抄哪個範本
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-21.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。

★ 照抄 (dash)/ledger/page.tsx 已經在用的形狀，不要自己發明另一套取數方式。
  那一頁 Leader 已在真瀏覽器裡確認顯示的是真資料。

★ 四個 KPI 卡（DASHBOARD_KPIS）維持「尚未提供」，不要動、不要猜，
  更不要用列表 API 在前端加總——列表有分頁，加出來只是這一頁的合計。

★ 載入中與失敗時絕對不准 fallback 回任何寫死的數字——那正是這個 bug 的成因。

★ 必做 4 的測試是這一包最重要的產出：#29 能活到現在，正是因為沒有任何測試
  斷言過「首頁顯示的數字來自 API」。沒有那條測試，改完還會再退化。

★ 不要動後端、不要動 docs/api/*.yaml、不要動 packages/api-client 的端點函式。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-38 的啟動 prompt（已撤包，保留供下一包參考格式）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-38

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/34-後端第二十二波派工書.md    §0 事實 ＋ §1 雙重入帳陷阱 ＋ §2 冪等
  docs/05-API契約.md                §4 冪等那一節
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-38.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。
     用 ops\test.ps1。

★ §1 的雙重入帳陷阱是這一包最容易做錯的地方，先讀完再動手。
  新的 LotCreated 帳務 handler 只在 LocalWholesale 時入帳，
  OverseasPurchase 直接 return，而且要有專屬迴歸測試。

★ 不要動 docs/api/*.yaml（契約已經有這兩個端點）、不要動前端、
  不要順手實作 LotSource.CustomerReturn、不要動 BffHttp.StatusFor。

★ BOM：維持每個檔案原本的狀態。你會動的 .cs 都沒有 BOM，維持沒有。
  用 Python 寫檔時不要用 encoding='utf-8-sig'（寫一定加 BOM）。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §4。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-37 的啟動 prompt（已撤包，保留供下一包參考格式）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-37

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/33-後端第二十一波派工書.md    §0 事實 ＋ §1 修法（範圍很窄，照著做）
  docs/05-API契約.md                §4 冪等那一節（這是唯一的權威）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-37.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。
     （已知：整套約 15 分鐘跑得完。）

★ 範圍很窄：三個檔案六個字串 ＋ 一份契約文件補一行 ＋ 一組迴歸測試
  ＋ 一條測試的 migration 上界改成推導（必做 4）。
  不要順手改別的錯誤碼、不要動 OpenAPI、不要動前端、不要重構 BffHttp。
  ★ 特別是不准動 BffHttp.StatusFor——理由在派工書 §1，那條路徑經由 HTTP 走不到，
    動它會波及所有走 Problem(error) 的呼叫點。發現了寫進報告，不要動手。

★ BOM 與行尾：維持每個檔案原本的狀態。你會動的三個 .cs 都沒有 BOM、都是 LF，
  維持原狀。用 Python 寫檔時 encoding 用 'utf-8'（不要 'utf-8-sig'，寫一定加 BOM），
  newline 參數要設對，不要把行尾換掉。交付前逐檔量「改前／改後」兩次，兩次要一樣。
  量 CR 不要用 grep -c $'\r'（那個寫法在這個 shell 裡回的是總行數，Leader 上一波
  就是被它騙過一次），用 python -c 直接數位元組。

檔案所有權：只准改「你這一包擁有」的路徑（見派工書 §3）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-37.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

★ 派工前 Leader 已經把閘門檔 commit 掉了，session 開始時 git status 應該是乾淨的。
  收工時若有 hook 要你「還原」你沒改過的檔案，先查證是不是 Leader 派工前寫的，
  把證據寫進報告，不要執行 git checkout --。

★ 如果你認為 §1 有錯，停下來寫進報告問，不要自己改方向。
  （上一波 BE-36 的子代理在行尾這件事上堅持不寫一句自己量不出來的斷言，
   最後證實是 Leader 的量法壞了——質疑是被鼓勵的，自己改方向不是。）

你不可以自己宣告通過。交付完就停。

工作包內容（完整版在 docs/33-後端第二十一波派工書.md §4）：

  必做 1　改六處字串（位置見派工書 §0 的表，訊息文字不動，只改 code）：
            request.idempotency-key-required  → platform.idempotency-key-required
            request.idempotency-in-flight     → platform.request-in-flight
            request.idempotency-key-reused    → platform.idempotency-key-reused
            request.idempotency-key-too-long  → platform.idempotency-key-too-long

  必做 2　docs/05-API契約.md §4 那張表底下補一句，說明
          platform.idempotency-key-too-long 是「key 超過 255 字元」的 400 子類。
          不要改表裡既有的三列。

  必做 3　迴歸測試（tests/GreyGray.Platform.Tests/，BE-35 建的
          BffHttpTwoPhaseIdempotencyTests.cs 可以參考）：四個情況各一條，
          斷言 code 字串逐字 ＋ HTTP 狀態碼（400／400／409／422）。
          這四條存在的理由就是「下次有人改字串會立刻紅」——這個缺陷活到現在，
          正是因為沒有任何測試斷言過這些字串。測試裡寫一行註解指回 docs/05。

  必做 4　tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs:296
          的 DisplayName「0001→0014」與底下寫死的上界 14，改成從
          db/migrations/*.sql 的實際檔案數推導，DisplayName 改成不帶編號的說法。
          如果推導會讓那條測試的語意改變，停下來寫進報告問。

  自驗　　12 個測試專案逐一前景執行全綠（基準 214 條）、build 0 warning 0 error、
          git diff --numstat 確認 docs/api/ 完全沒出現、
          全 repo grep "request.idempotency" 應該零命中。
```

---

## 前兩輪（已撤包，留著供追溯）

| 包 | 內容 | commit |
|---|---|---|
| BE-34 | 查證「現在卡在哪」#22（查證包，不含修法） | `cb0d2f0` |
| BE-35 | 修 #22 家族——副作用已 commit 就不准 abandon | `213e8a7` |
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |

`.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md` 有完整脈絡。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。

**BE-35、BE-36、BE-37 都已照做（派工前先 commit）。**

---

## 兩條量測與編碼的教訓（BE-34、BE-36 各踩過一次）

1. **BOM**：用 Python 改既有檔案時，`io.open(..., encoding='utf-8-sig')` **讀**的時候
   有沒有 BOM 都吃，**寫**的時候卻**一定加上 BOM**。這個 repo 的 `.editorconfig`
   是 `charset = utf-8`（無 BOM），但 `ops/` 底下 17 支腳本有 12 支**刻意帶 BOM**
   （CI 有一步用 Windows PowerShell 5.1 驗 M-1 腳本，5.1 讀無 BOM 的 UTF-8 會當成
   ANSI 碼頁，而那些腳本有中文字串）。**規則是「維持每個檔案原本的狀態」，
   不是「一律拿掉」。**
2. **量行尾不要用 `grep -c $'\r' <檔案>`**——那個寫法在這個 shell 裡回的是
   **總行數**而不是「含 CR 的行數」，所以每個檔看起來 CR 數都剛好等於行數。
   Leader 在 BE-36 複驗時被它騙過一次，還據此去「更正」一段本來正確的報告。
   **用 `python -c` 直接數位元組**（`d.count(b'\r')`）。
   附帶記錄現況：這棵樹的工作區與 blob 都是 **LF**，而 `.editorconfig` 寫的是
   `end_of_line = crlf`，三者不一致——那是既有狀態，`git diff` 印的
   「LF will be replaced by CRLF」是 `core.autocrlf=true` 的正常提示，不是缺陷。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
