# 決策紀錄（ADR）

一條一個決定。**已決定的不要重新討論**，要推翻就新增一條並標註取代哪一條。

---

## ADR-001　技術棧採用 .NET 10 LTS
**狀態**：已採納（2026-08-27，經三方外部評估）

C# / .NET 10 ＋ ASP.NET Core ＋ PostgreSQL 17+。次選是 Kotlin + Spring Boot 4 / Spring Modulith。

**為什麼**：託管記憶體安全；nullable reference types 把 null 錯誤推到編譯期；`internal` 是真正的組件級私有，
讓模組硬邊界能被編譯器執行；.NET 10 是 LTS（三年支援）。

**代價（誠實記下）**：YC 上完全沒裝 dotnet，現有生態是 Node（3 個 Next.js ＋ 1 個 NestJS）加 Python。
引入 .NET 代表第三套部署腳本、第三組監控指紋、團隊肌肉記憶的斷點。
但那是**一次性成本**；而 TypeScript 的 `number` 是 IEEE-754、型別在執行期被抹除，
是**每天都在的成本**，且它會在帳務出錯時才發作，那時已經是客訴了。

**若日後改用 Node**：底線是金額一律用 `bigint` 最小單位、Ledger 的不變式全部下沉到 Postgres
constraint 與 trigger，不靠應用層守。

---

## ADR-002　正式站用 YC（自架筆電），不上雲
**狀態**：已採納（2026-08-27）

已聽過四項顧慮（筆電無 UPS、家用版無法停用自動更新、與 Telegram bot 及 Ollama 共用攻擊面、
雲端 VPS 月費約 NT$500），決定先用 YC，量大到撐不住再搬。

**七項升級為必要條件**（不是選配，它們是這個決定成立的前提）：
1. Postgres data 與 WAL 放 C 槽 NVMe，D 槽只放備份與歸檔
2. 接有線網路，Wi-Fi 降為備援
3. 買一顆 UPS（NT$2,000 級，目的是安全關機不是續航）
4. Windows Update 改手動 ＋ 維護窗，prod-monitor 加「開機時間異常變動」告警
5. GreyGray Platform 用獨立的 Windows 使用者帳號執行，不要用 `moera`
6. Ollama 限制在營業時間外，或限制模型大小到能完整放進 4 GB VRAM
7. M1 上線前必須完成 ngrok → cloudflared 遷移

**搬雲觸發門檻**（任一成立就評估，做進 prod-monitor 告警）：
日訂單量持續 > 300 張、DB > 20 GB、一個月內非計畫停機累計 > 30 分鐘、客戶個資 > 5,000 筆。

---

## ADR-003　部署用 Native ＋ NSSM，不用 Docker
**狀態**：已採納（2026-08-27，推翻 v0.1 的 Docker Compose 方案）

YC 是 Windows 11 家用版，沒有 Hyper-V；Docker Desktop 只能走 WSL2 backend，本身要吃 2–4 GB 記憶體。
更重要的是現有 12 個服務**全部是 native process**，用排程任務（S4U ＋ BootTrigger ＋ 每 5 分鐘
watchdog ＋ RestartOnFailure 999）與 NSSM 管理，而且做得很紮實。引入 Docker 等於讓兩套維運模型並存。

改為 `dotnet publish -r win-x64 --self-contained` 產出獨立資料夾，以 NSSM 註冊成 Windows service。

**技術更正**：ASP.NET Core 可直接跑 Windows Service（`UseWindowsService()`），不必硬套 NSSM。
兩者擇一，**不要混用**。與既有 12 個服務一致的話用 NSSM 也合理。

---

## ADR-004　模組維持硬邊界（assembly ＋ schema ＋ DB role 三重分離）
**狀態**：已採納（2026-08-27）

三個外部評估都指出「14 個限界上下文對一人團隊是作繭自縛」。已看過這些意見，
決定維持硬邊界——前期紀律換後期速度。

**停損線（寫下來是為了讓將來的你有一個不必自責的退出點）**：
如果 M0 做超過三個月還沒能讓一個 hello-world 模組跑完「發事件 → 被消費 → trace 完整」，
那不是你慢，是這整套儀式對現階段太重了。屆時請把 assembly 分離與 DB role 分離**降級成
namespace 邊界**，保留意圖、砍掉儀式。

---

## ADR-005　第一版直接對客人開放，主力是出國採購團
**狀態**：已採納（2026-08-27）

不做內部試用期。現貨模式（STOCK）延到 M2。

為了換取風險空間，三件事移出第一版：費率先寫死不做規則引擎、只做綠界一家金流、現貨延到 M2。

---

## ADR-006　多租戶只留欄位，不做隔離
**狀態**：已採納（2026-08-27）

所有 tenant-scoped 資料表加 `tenant_id NOT NULL`，預設填 `TenantId.Default`。
不做 RLS、不做後台切換、不做租戶管理 UI。

**現在唯一要做對的一條**：`platform.outbox_message` 必須自帶 `tenant_id`。
dispatcher 是背景程序，不在任何請求裡，沒有使用者上下文可推。
這張表一旦上線帶了資料，之後補欄位還得回填歷史訊息。

其餘三個坑（RLS 的 owner/BYPASSRLS 陷阱、pgBouncer 的 `SET LOCAL`、Ledger 的同租戶 constraint）
是啟用隔離那天才需要，屆時再加不影響既有資料。

---

## ADR-007　售價內含服務費 → 會計實質是買賣，不是代理
**狀態**：已採納（2026-08-27）

客人只看到「售價 ＋ 運費」兩項。因此兩種模式（採購團／現貨）**用完全相同的一組科目**，
Ledger 的實作量、測試量與心智負擔少了一半。匯兌損益科目也不需要——現場刷卡即時結清，
存貨成本就是刷卡帳單上的台幣金額。

**必須知道的稅務含意**：這個定性實務上**不可逆**。將來若辦營利事業登記，發票要就全額開立，
營業稅按售價全額課，而不是只課服務費。帳上沒有代收代付的軌跡，就無法主張代理關係。
若日後要回到代理模式，要改的不只是系統，而是商業做法本身。

---

## ADR-008　Payment 不記帳
**狀態**：已採納（2026-08-27）

Payment 只負責「跟金流商互動並回報結果」。它發 `PaymentCaptured`，Ledger 訂閱後開分錄。
**Ledger 是唯一寫入帳務事實的模組。**

這個分工必須從第一天守住，否則半年後你會有三個地方在算錢，而且互相對不上。

---

## ADR-009　前台採用「韓系柔美 Soft Seoul」
**狀態**：已採納（2026-08-28）

五套彩色系樣板中選定。切版是圓角卡片牆：圓角搜尋列 ＋ 頭像、柔粉漸層 banner ＋ 圓形產品、
橫捲圓形分類標、商品卡帶 NEW／人氣標籤與收藏愛心。全站沒有一個直角。

**為什麼是它**：最貼近藥妝／美妝品項的調性，而且五套裡前端工時最低（2/5）。
資訊密度、互動回饋、品牌記憶都在中上（各 4/5），沒有明顯短板。

前後端分離，**換前端不動 API**。所有樣板共用同一組研究導出的 UX 規則
（手機優先、常駐底部加購列、每個商品配 1–2 句描述、顯示單位價格、統一 1:1 裁切、
購物袋顯示含運總額）。

---

## ADR-010　M1a 運費一口價：超商 60／宅配 120
**狀態**：已採納（2026-08-28）

不算材積、不分級距。M1a 只需要這兩個數字。

**要留意的分別**：你付給物流商的成本，與你向客人收的運費，是兩個獨立的數字。
物流商 API 給的是前者，後者是商業決策。Ledger 已把它們分成「運費成本」與「運費收入」
兩個科目，月結時運費是賺是賠自己會浮出來——如果發現大件商品每單都在貼錢，
那就是啟用 M3 材積重計費的訊號。

即使費率寫死，`pricing_snapshot` 仍要完整記錄「當時收了多少、依據什麼」，
否則 M3 導入規則引擎時無法回溯比對。

面交／自取的運費是 0，M1a 就支援。

---

## ADR-011　金流分階段：M1a 只做綠界
**狀態**：已採納（2026-08-27）

三家都要，但分階段。M1a 只做綠界——它一家包辦金流、超商取貨、宅配，將來要開發票也不用換廠商。
藍新與 LINE Pay 留到 M3，屆時 adapter 介面已被一個真實實作驗證過。

三家同時開，等於在還沒驗證任何一家之前就先付抽象化的代價。

**連帶影響（M3 才處理，但現在要知道）**：
- LINE Pay 不支援超商取貨與宅配，Checkout 必須在客人選付款方式時就過濾可用配送方式。
  這是一條真實的業務規則，不是 UI 細節。
- Ledger 的「現金」要拆成三個在途子科目（撥款週期與手續費不同，混在一起對不起來）。
- 對帳變成三份，差異告警也要分家。
- 三套 webhook 簽章驗證，Cloudflare Queue 要開三條。

---

## ADR-012　客人對漲價詢問的回覆，走 Procurement 自己的端點
**狀態**：已採納（2026-08-28，本次設計決定，藍圖未涵蓋）

LINE 的 postback 經 Storefront BFF 直接打到 `Procurement.IInquiryReplyReceiver`，
**不由 Notification 發事件回去**。

**為什麼**：支撐群（Notification、Audit、Reporting）只訂閱事件、不被任何人依賴——
這是判斷模組切得對不對的快速檢查。若 Procurement 訂閱 Notification 的事件，
就出現了「支撐模組被依賴」，邊界破了。

Notification 的職責因此收斂成純粹的「送出去」。詢價軌跡（問了、幾點問的、客人有沒有回）
記在 Procurement 的 `inquiry` 表——糾紛時要拿出來的就是那份。

---

## ADR-013　DeliveryMethod 定義在 Pricing.Contracts
**狀態**：已採納（2026-08-28，本次設計決定）

配送方式這個列舉，Pricing、Checkout、Ordering、Fulfillment 四個模組都需要。
放在 Fulfillment 會造成 Pricing ↔ Fulfillment 的循環參考（運費按配送方式計，
所以 Pricing 一定要看到它）。

放進 Shared.Kernel 也不行——那裡的規則是「只放無業務語意的型別」。

因此放在 Pricing.Contracts，它是需要這個概念的最底層模組。
`GreyGray.Architecture.Tests` 有一條測試專門斷言 Contracts 之間的相依無環。

---

## ADR-014　Contracts 之間允許相依，但必須是 DAG
**狀態**：已採納（2026-08-28，本次設計決定）

模組的 `*.Contracts` 可以參考其他模組的 `*.Contracts`（否則事件裡只能傳裸 `Guid`，
型別安全就沒了）。但這個相依圖**必須無環**，層級如下（後面的只能依賴前面的）：

```
Identity, Catalog
  → Notification, Audit
  → Pricing, Campaign
  → Inventory
  → Checkout
  → Ordering
  → Fulfillment, Payment, Procurement, Ledger, Reporting
```

`*.Core` 之間**永遠不可以**互相參考，這條沒有例外。

---

---

## ADR-015　專案定名 GreyGray Platform
**狀態**：已採納（2026-08-28）

原本的工作名稱是「代購平台 / daigou-platform」。定名為 **GreyGray Platform**。

改名範圍是全面的，不是只有資料夾：

| 層面 | 舊 | 新 |
|---|---|---|
| 目錄 | `01_開發中_wip\daigou-platform` | `01_開發中_wip\GreyGray_Platform` |
| 方案檔 | `Daigou.slnx` | `GreyGray.slnx` |
| 組件與命名空間 | `Daigou.*` | `GreyGray.*` |
| Postgres 角色 | `daigou_app` · `daigou_owner` · `daigou_<schema>` | `greygray_*` |
| 連線字串鍵 | `ConnectionStrings:Daigou_<schema>` | `ConnectionStrings:GreyGray_<schema>` |
| 知識庫命名空間 | `代購平台` | `GreyGray_Platform` |

**為什麼一次改乾淨**：改名當下沒有任何東西部署出去、沒有資料庫跑過 migration、
沒有外部系統引用，所以成本接近零。半套改名（資料夾叫 GreyGray、程式碼叫 Daigou）
會讓每一個接手的人都要問一次「這兩個是同一個東西嗎」，那個成本是永久的。

「代購」這個詞仍會出現在文件裡，但它從此是**業務領域的描述**，不是專案名稱。
唯二保留舊名的地方是外部資源的實際標題：藍圖 artifact 與 planner 提醒。

---

## ADR-016　`platform` schema 是刻意的共用例外，outbox 由模組自己的 DbContext 寫
**狀態**：已採納（2026-08-28，本次稽核發現三份文件互相矛盾後補的裁決）

原本三句話同時存在而且不可能同時成立：

1. M0-1：「`PublishAsync` 只寫 outbox，**且必須參與呼叫端的交易**（同一個 DbContext／同一條連線）」
2. M0-1：「產出 `src/Platform/PlatformDbContext.cs`」
3. M0-5：「每個模組一個 DbContext，**不要**在任何一個 DbContext 裡 map 別的模組的表」

若 outbox 只存在於獨立的 `PlatformDbContext`，它就是另一條連線、另一個交易，
第 1 條的原子性保證直接失效——事件會在業務資料 rollback 之後照樣送出去。

**裁決**：

- `platform` schema 的三張表（`outbox_message`、`processed_message`、`saga_timer`）
  由 `GreyGray.Platform` 提供一個 `ModelBuilder` 擴充方法，**每個模組的 DbContext 都套用它**。
  對應的 EF 實體型別由 Platform 擁有，模組不得自己重新定義。
- 這**不是**破例跨 schema JOIN。禁止的是跨 schema **JOIN 業務資料**；
  這裡是同一條交易寫兩張表，沒有任何 JOIN，而且 0001 已經把 platform 的權限
  明確 GRANT 給 14 個模組 role，資料庫層本來就承認這個例外。
- `PlatformDbContext` 仍然存在，但它的職責收斂成**只有背景與 BFF 才用得到的部分**：
  dispatcher 的批次撈取、`idempotency_key`、saga timer 掃描。它用 `greygray_platform` 這個 role。
- `idempotency_key` 不需要參與業務交易——它的協定是 begin → 做事 → complete 三段式，
  本來就跨交易。

**M0-5 那句話的正確版本**：不要在任何一個 DbContext 裡 map **別的業務模組**的表。`platform` 除外。

---

## ADR-017　支撐模組的精確規則：不被業務模組依賴；支撐之間可以
**狀態**：已採納（2026-08-28，本次稽核發現原規則與事件目錄自相矛盾後補的裁決）

原本的說法是「支撐群（Notification／Audit／Reporting）**只有進箭頭，沒有出箭頭**」。
但 `docs/02-事件與狀態機.md` 的事件目錄自己就有三條出箭頭：
`notify.NotificationSent → Audit`、`reporting.ReportGenerated → Notification`、
以及 Notification 要訂 `ledger.LiabilityExceededCash`。含糊放著，平行開發的人會各自解讀。

**精確版本**：

- **業務模組（其餘 11 個）不得參考 Notification／Audit／Reporting 的任何組件。** 沒有例外。
  由 `Architecture.Tests` 的 `Business_modules_must_not_depend_on_support_modules` 斷言。
- **支撐模組之間可以互相訂閱事件。** 那不算破口——業務模組沒有因此被綁住，
  而「拆掉 Reporting 不影響任何業務模組」這個性質仍然成立。

**連帶處置：`IAuditWriter` 移到 `GreyGray.Platform.Abstractions.Audit`。**

個資存取留痕是**同步**的：`ICustomerDirectory.GetContactAsync` 讀出客戶明文的那一刻就要留一筆，
事後補事件等於留下一段沒有稽核的空窗。若 `IAuditWriter` 留在 `Audit.Contracts`，
`Identity.Core` 就必須參考支撐模組，上面那條規則第一天就要開例外。

稽核寫入實際上是**橫切的平台能力**，不是模組能力，所以它屬於 `Platform.Abstractions`。
`AuditCategory` 一起搬。參數改用 `Guid?` 而非 `StaffId?`／`CustomerId?`——
`Platform.Abstractions` 只能參考 `Shared.Kernel`（有測試斷言），而稽核本來就是泛型接收端。

`Audit.Contracts` 留下的是查詢面：`AuditRecord`、`IAuditQuery`、`AuditRecorded`。

---

## ADR-018　JSON 線上格式一次定死，來源唯一
**狀態**：已採納（2026-08-28，本次稽核）

**問題**：44 個整合事件會由多人（或多個 agent）平行寫。System.Text.Json 的預設行為會把
`readonly record struct CustomerId(Guid Value)` 序列化成 `{"value":"…"}`、把 enum 序列化成數字。
各自帶各自的 `JsonSerializerOptions`，payload 形狀就會分歧。

**而 outbox 裡的 JSON 一旦有正式資料就改不動了**——改形狀等於讓所有未派送的訊息反序列化失敗。
所以這件事必須在寫第一行業務邏輯之前定死。

**唯一來源**：`GreyGray.Shared.Kernel.Json.GreyGrayJson.Options`。
outbox payload 與 HTTP API **用同一組設定**。

| 型別 | 線上形狀 |
|---|---|
| property 名稱 | camelCase |
| 所有 GUID —— **強型別 `XxxId` 與裸 `Guid` 都算** | 字串，32 字元十六進位，無連字號：`"0198c3d4…"` |
| `Money` | `{"amountMinor": 18000, "currency": "TWD"}` |
| 所有 enum | 字串（`"ConvenienceStore"`，不是 `1`）|
| `DateTimeOffset` | ISO 8601 含位移 |
| `DateOnly` | `"2026-08-28"` |
| null | **照寫不省略** |
| 中日韓文字 | 不轉義（否則備註與姓名在 DB 裡變成 `\uXXXX`，SQL 查不動）|

**金額為什麼用 JSON number 而不是字串**：`long` 最小單位的值域遠在 IEEE-754 安全整數
範圍內（±2^53），JavaScript 端 `JSON.parse` 不會失真。會失真的是「元」為單位的小數，
而那正是本專案不用 `decimal` 的原因。

**不提供「元」欄位**：多一個衍生欄位就多一個對不起來的機會。顯示是前端的事。

**裸 `Guid` 為什麼也要管**：`IIntegrationEvent.EventId` 是 `Guid` 不是強型別 ID，
System.Text.Json 預設會寫成帶連字號的形式。放著不管的話同一個 payload 裡會有兩種格式，
而 `eventId` 正是消費端去重的 key（`platform.processed_message` 的主鍵之一）。
一邊用 `Guid.Parse` 比、一邊用字串比，就會出現「同一則訊息被處理兩次」，
而那種 bug 只會在正式環境的重送路徑上出現。2026-08-28 覆驗時實測抓到，已修。

---

## ADR-019　不遷移歷史會員與訂單；改用 Google 帳號串接降低重新註冊的摩擦
**狀態**：已採納（2026-08-28）

**問題**：舊的租用平台不給匯出，要匯出得再付一筆。實際查到的價格是
**年繳才 NT$1,399，非年繳 NT$2,599**——為了把資料搬出來而綁一整年的租約，
成本不成比例（新系統上線後那個平台就只剩查詢用途）。

**決定**：**不遷移。** 新系統只收新單，舊平台留著查到租約到期。
客人在新站重新註冊，並在**之後**加上 **Google 帳號串接**降低重新註冊的摩擦。

**為什麼 Google 而不是硬遷**：重新註冊的痛點是「又要填一次表、又要記一組密碼」，
不是「資料不見了」（會員自己並不會想調出三年前的訂單）。
用 OAuth 把註冊變成兩次點擊，痛點就沒了，而且省下的不只是 NT$1,399——
是「舊資料的髒欄位進到新 schema」這個長期成本。

**排程**：M1a 仍然是**手機號碼 ＋ 密碼**，Google 與 LINE 都排在之後。
`Me` 已經有 `lineLinked` 這個外部綁定旗標的形狀，Google 之後照同一個模式加
`googleLinked` 即可，**是加欄位不是改欄位**，不算破壞性變更。

**連帶解鎖**：`POST /v1/auth/register` 的欄位不再是暫定。
現行的 phoneNumber / password / displayName / email / referralCode 就是定案，
`Identity` 的 schema 可以直接設計，不必等匯出檔。

**儲值金：沒有這個問題**（2026-08-28 由老闆確認）。
舊平台**從來沒有啟用過儲值金功能，所有餘額都是 0**，沒有任何要搬過來的負債。
新系統的儲值金功能照原計畫做（退款退成儲值金零手續費是 M1a 的賣點之一），
**期初餘額一律從 0 開始**——不要因為「代購生意通常有儲值金」就自己假設要做遷移或對帳。

**唯一的代價**：客人體驗的一次斷點。老客人第一次來會發現要重新註冊、看不到舊訂單。
上線公告與註冊頁要講清楚「舊訂單請到原平台查詢，查詢期限到 ____」——
那個日期是舊平台的租約到期日，要去查出來填上。

---

## ADR-020　單一品項缺貨使用 Unavailable，退款金額進入兩側訂單投影
**狀態**：已採納（2026-08-29，M1a-6 契約補漏）

`POST /v1/orders/{orderId}/lines/{lineId}/cancel` 的 frozen description 明寫
「現場缺貨時用」，所以該 line 轉 `Unavailable`；整張訂單取消時才把尚未完成的
line 轉 `Cancelled`。兩者不靠自由文字 `reason` 判斷。

Admin 的 `AdminOrderLine` 新增可空 `refundedAmount`，與 Storefront 對稱。
缺貨補償不做部分數量：整條 line 的退款額固定為 `lineTotal`；其餘 line 與訂單狀態不變，
`GoodsTotal`／`GrandTotal` 扣掉該 line，`ShippingFee` 保留。Payment 累計退款額並以
`PartiallyRefunded`／`Refunded` 區分；Ledger 只在 `PaymentRefunded` 事實成立後開平衡分錄。

原路退款 provider 尚未接妥時，已付款訂單一律在 BFF 擋下，不得先改狀態或先記帳。

---

## ADR-021　Admin 瀏覽器跨 origin 只開明確白名單
**狀態**：已採納（2026-08-29，M1a-6 串接補漏）

Admin 前端在 mock 關閉後會從 `:5003` 直接呼叫 Admin BFF，因此 BFF 必須處理瀏覽器
preflight。Development 僅預設允許 `http://localhost:5003` 與 `http://127.0.0.1:5003`，
並允許 credentials；Production 沒有預設來源，必須用 `Cors:AllowedOrigins` 明確設定。
不得使用 `AllowAnyOrigin`，也不得把 credentials 與萬用來源混用。

---

## ADR-022　快取／session 儲存改用 Microsoft Garnet，不用 Valkey
**狀態**：已採納（2026-08-29，YC 上實測後）

**背景**：ADR 原本寫 Valkey。實際要在正式機 YC（Windows 11 家用版）安裝時才發現，
**Valkey 官方沒有任何 Windows binary，也沒有計畫要做**——官方安裝文件只列
Linux／macOS／WSL／Docker，而 WSL 與 Docker 都違反 ADR-003（Native ＋ NSSM）。

查過的三條路：

| 選項 | 為什麼不選 |
|---|---|
| Layerbase 的社群 build | 綁 Cygwin，沒有公布版本號與 checksum，只能透過他們的 app 或 npm 取得 |
| Memurai Developer | 原生 Windows、winget 可裝，但 **Developer 版不授權正式環境**，正式用要買 Enterprise |
| `Redis.Redis` 3.0.504 | 微軟 2016 年就停止維護的移植版 |

**決定用 Microsoft Garnet**（`winget install Microsoft.Garnet.DN8`）：

- **MIT 授權、微軟開源**，正式環境免費
- **原生 Windows**：.NET 寫的，不需要 Cygwin、WSL 或 Docker
- **RESP 線上協定**，未修改的 Redis client 直接可用——包含這專案在用的
  `StackExchange.Redis`（`AddStackExchangeRedisCache`）。**應用程式一行都不用改**
- winget 安裝有雜湊驗證，符合 BE-10 腳本「來源要能驗證」的要求

**YC 上的實測**（2026-08-29）：綁 `127.0.0.1:6379` 啟動後，用原始 TCP 送 RESP `PING`，
回 `+PONG`。winget 的 DN8 套件會順帶帶入 .NET 8 runtime（`NETCore.App 8.0.30`），
`net9.0` 那份因為沒有 .NET 9 runtime 起不來，用 `net8.0` 那份。

**這個決定的影響範圍很小，因為它扛的東西比藍圖寫的少。** 查過原始碼：

| 藍圖說 Valkey 要做 | 實際上 |
|---|---|
| session | ✅ 只有這個真的接了（`DistributedSessionStore`） |
| 鎖 | ❌ 已經在 Postgres（Worker 用 advisory lock 1002） |
| rate limit | ❌ 還沒接，程式裡 0 筆 |

也就是說它現在只存後台 session——一份「高頻讀取、丟了最壞是重新登入」的資料。

**沒有選「乾脆不要這個服務、session 存 Postgres」的理由**：那要改程式，而 Garnet 是零改動。
等 M3 要接 rate limit 時再重新評估要不要留這一層。

**連帶要改的**：`ops/install-environment.ps1` 的 Valkey 段、
`ops/verify-environment.ps1` 的 5 項 Valkey 檢查、`docs/14` runbook。

---
