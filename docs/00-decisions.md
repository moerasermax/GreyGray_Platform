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

**適用範圍澄清（2026-08-30，老闆裁決）：本條約束的是「正式機 YC」，開發機不限。**

上面每一條理由——YC 沒有 Hyper-V、記憶體只有那麼多、既有 12 個服務全是 native、
不要讓兩套維運模型並存——**講的都是正式機**。開發機沒有任何一條成立。

觸發這次澄清的是 BE-22：它要在開發機上架本機 PostgreSQL，實測發現 Windows 上的
`postgres.exe` **內建拒絕以 Administrator 身分啟動 server**
（`Execution of PostgreSQL by a user with administrative permissions is not permitted`），
那是寫死的 `IsUserAnAdmin()` 檢查，不是設定問題。正式機用專屬非管理員帳號繞過；
開發機要照做就得在老闆自己的機器上新建 Windows 帳號並調 ACL，代價比問題本身大。
BE-22 改用 `postgres:17-alpine` 容器，**沒有先斬後奏**，標明「請整合者確認」後才交付。

判斷依據：

- **正式機那條路徑完全沒動**——`install-environment.ps1` 的原生安裝流程原封不動
- **不是新引入的依賴**——這個 repo 的整合測試（`Testcontainers.PostgreSql`）本來
  就在用同一顆 `postgres:17-alpine` 映像
- 官方 ZIP binaries 仍然解壓，只是改當 `psql`／`pg_isready` **客戶端工具**用；
  `ops/invoke-migrations.ps1` 一行都不用改（它只認 host/port）

**所以：正式機部署不得引入 Docker（原文不變）；開發機用什麼跑相依服務不受本條約束。**
寫下這一句是為了讓下一個人不必重新辯一次——他看到開發機有 Docker 容器時，
應該在這裡讀到「是的，這是刻意的」，而不是以為有人違規。

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

---

## ADR-023　缺貨退款去向由客人選；M1b 只開原路，流程一次建到位
**狀態**：已採納（2026-08-29，BE-11 派工前拍板）

**背景**：兩份已採納的文件對同一件事說了不同的話，不是二選一的偏好問題。

| 來源 | 說的是什麼 |
|---|---|
| 藍圖 · 補償路徑 | 「該 OrderLine 取消並退款（**客人自選**原路或儲值金）」 |
| 後台凍結契約 ＋ `docs/06` FE-8 | `refundTo` 兩個選項，**營運在後台代選**，預設儲值金 |
| 藍圖 · 落地里程碑 | **儲值金排在 M3** |
| ADR-019 更正（`0add0dc`） | 「退款退成儲值金零手續費是 **M1a** 的賣點之一」 |

也就是說：M1b-2 需要一個退款去向，而預設的那個去向排在 M3 才做。

**決定一：去向由客人自己選，不是營運代選。** 與藍圖字面一致。

**決定二：M1b 只開原路退款，但流程先建起來。**
契約裡 `refundTo` 兩個值都在、客人選擇的流程也建好，
但 M1b 時選 `StoredValue` 要回**可預期的業務失敗**（`Result`，不是例外），
訊息明講「儲值金要到 M3 才開放」。M3 把儲值金做完就直接開啟，不必回頭改流程與契約。

**為什麼不把儲值金最小面從 M3 拉到 M1b**（當時建議過這條）：
拉過來會在 M1b 就產生「客戶儲值金」這個負債科目的實際餘額，
而儲值金的到期規則、儲值入口、餘額查詢全都還沒設計。
**先產生負債、後補規則，是帳務上最難收拾的順序。** 寧可 M1b 每筆退款付一次手續費。

**連帶的三件事：**

1. `refundTo` 的語意從「營運選」變成「客人選」是**契約異動**，走 `docs/05` 的流程，
   由整合者提。**BE-11 不得自己改契約。**
2. 前端 FE-8 現有的取消對話框是「營運選、預設儲值金」，與這個決定不符，**要回工**。
3. **不得默認任何一種去向**——`docs/10` §3 原本的禁令仍然有效。
   在客人選出來之前，`RefundRequested` 不發。

**這條解除了 BE-11（M1b-2）的阻塞。**

---

## ADR-024　ADR-023 的補正：原路退款不是既有能力，M1b 要補綠界退款 API
**狀態**：已採納（2026-08-30，第六波整合驗收後）

**ADR-023 有一個沒查證的前提。** 它決定「M1b 只開原路退款」時假設原路可用，事實不是：

- `M1aEndpoints.cs:204`／`:290`：兩個 cancel 端點對**已付款**訂單一律拒絕原路退款
- `Payment/OrderingEventHandlers.cs:43`：`throw NotSupportedException`
  「綠界原路退款尚未具備凍結的 provider API 與必要設定；**不得把退款要求標成成功**」

那個 `throw` 是對的——假裝退款成功比拒絕更糟。錯的是 ADR-023 沒去看它。

FE-14 照 ADR-023 把儲值金在 UI 停用之後，**兩條路都斷了**：
營運只能選原路，而原路必定失敗。**回工前反而是通的**（預設儲值金、後端接受）。

**決定：補綠界原路退款 API，ADR-023 的方向維持不變。**

連帶影響：**E3（綠界正式商店代號與金鑰）從「上線前」提前到 M1b**——
目前用測試憑證，而退款 API 需要能實際發動退款的商店設定。

**同一包一併做完 ADR-023 決定二**：`StoredValue` 在 M1b 要回可預期的業務失敗
（`Result`，訊息說「儲值金要到 M3 才開放」）。這條在整個 repo 目前**沒有任何地方實作**，
前端只擋在 UI，API 層仍收得下。要動的是 `OrderingApplicationService` 的取消退款路徑。

**為什麼不改用儲值金**（驗收時查到、也提給老闆選過）：儲值金退款其實整條都實作好了
（科目 2130、分錄 handler、`IStoredValueQuery`、前台 `/me/stored-value` 與錢包頁），
缺的只有「客人主動加值入口」與「到期規則」。但老闆選擇維持 ADR-023 的方向——
**真的把錢退回客人卡片**，而不是在到期規則未定的情況下開始累積儲值金負債。

---

## ADR-025　鑑賞期七天，簽收不等於完成
**狀態**：已採納（2026-08-30）

凍結契約 `openapi.admin.yaml`（`/v1/shipments/{id}/deliver`）明寫：
「送達不等於完成——訂單要等**鑑賞期**屆滿才轉 `Completed`，那條是 Saga Timer 的事」。
`docs/16` 第一版把它寫成「簽收後轉 `Completed`」是錯的，BE-14 停下來回報，已更正。

**但那個 Saga Timer 不存在，也沒有任何一包負責。** 沒有它訂單永遠停在 `Delivered`，
**D2「端對端流程走通」走不完**。

**決定：M1b 就做，另開一包。鑑賞期 7 天，對齊消保法第 19 條的通訊交易七日解約。**

- 天數做成**組態**不寫死，之後要調不用改程式
- 基礎設施已有：M0-3 的 `SagaTimerDispatcher`、advisory lock 1002、排程表
- 簽收只發 `fulfillment.ShipmentDelivered.v1`，**不得轉 `Completed`**

> 代購是否適用《通訊交易解除權合理例外情事適用準則》的例外，法律適用由老闆判斷；
> 系統只負責把選定的天數做成可調的組態。

---

## ADR-026　支援部分買到：買到的出貨、短缺的退款
**狀態**：已採納（2026-08-30）

M1b-1 目前對「部分買到」（客人訂 5 個、現場只買到 3 個）一律回 422 拒絕。
那是刻意的 fail-closed——短缺數量的退款規則沒人拍板，BE-11 拒絕自己發明，正確。

**決定：支援。買到的數量照常出貨，短缺的數量退款。**

理由：代購現場買不到足量是日常不是邊緣案例。維持 422 的話，
營運只能把整條標缺貨全額退，**客人本來拿得到 3 個卻拿到 0 個，而那 3 個已經花錢買了**。

**相依**：短缺退款要用到退款路徑，所以**必須排在 ADR-024（綠界退款 API）之後**。
影響範圍：契約（`quantityPurchased` 的語意與部分退款欄位）、`ItemPurchased` 事件、
Ordering 的部分退款、Ledger 分錄。是一整包。

---

## ADR-027　現場漲價詢問的逾時，做成每團可設
**狀態**：已採納（2026-08-30）

BE-11 用了 2 小時的技術預設值，並在 XML 註解明寫「需要人拍板」——沒有假裝那是決定，正確。

這個數字卡在兩邊：**買手站在店裡等不了太久，但客人可能在睡覺**，
而逾時的後果是「自動視為照買」——花的是客人的錢。

**決定：做成每團可設，開團時指定。** 日本藥妝店與精品店的節奏本來就不一樣。

影響範圍：Campaign 契約加欄位、開團 UI 加輸入、`PriceInquiryTimeout` 改讀團的設定。

---

## ADR-028　金額顯示統一用 `NT$`
**狀態**：已採納（2026-08-30）

契約的 `unitPriceLabel` 範例寫 `NT$780／32 顆`，共用的 `formatMoney()` 對台幣輸出 `$780`，
同一張商品卡上兩種寫法並存。這個從前端第一波拖到現在。

`$` 是**正確的 ICU 行為不是 bug**——`zh-TW` 是台幣的本地語系，
`Intl.NumberFormat('zh-TW', {currency:'TWD'})` 就給不帶國別前綴的 `$`，外幣才會帶（`US$`、`HK$`）。

**決定：改 `formatMoney()`，對 TWD 明確加 `NT$` 前綴。**

**不要在呼叫端各自加前綴**——那會變成散在幾十個元件裡的字串拼接。
只改 `formatMoney()` 一處，所有顯示一起變。

---

## ADR-019 更正（2026-08-30）：舊平台已停用，沒有到期日要公告

ADR-019 與 `CLAUDE.md` 都寫著「租約到期前要處理、**先把到期日寫下來**」。
**老闆確認：舊平台已經停用過期了，沒有這個日期。**

連帶：

- **E5 取消**。註冊頁與上線公告**不要**寫「舊訂單請到原平台查詢，期限到 ____」——
  那個查詢管道已經不存在。文案改成明講「舊平台已停用，歷史訂單無法查詢」。
- ADR-019 更正（`0add0dc`）已確認舊平台儲值金餘額為 0，所以**沒有跟著平台一起消失的錢**。
- `CLAUDE.md`「沒有系統會提醒你的那一件」那一段已經過時，一併更正。

---

## ADR-029　開發環境的付款用「綠界模擬器」獨立行程；正式碼只多一道網域守衛
**狀態**：已採納（2026-09-02，使用者拍板「先做，但要能隨時更換回 adapter，因為可以更換 adapter 就可以上了」）

**問題**：D 階段「一條完整流程走得完」卡在付款。正規路要等 E3（綠界商店代號）＋ E2（對外可達的回呼網址），
兩個都不在我們手上；而付款之後的整段路（訂單、出貨、鑑賞期、退款、分錄）從來沒有人用畫面連續走過。

**否決的三個做法**：
- 「模擬付款成功」開關——會在正式碼裡放一條繞過真實付款的路徑，之後要靠紀律確保它不流到正式環境。
- 借用 `PaymentProvider.ExternalSettled`——契約寫明那是 M5 通路訂單（蝦皮代收）的位置，借用會汙染語意。
- 寫一個假的 `IEcpayGateway`——那是在測試裡才該存在的替身；放進 dev 會讓「換回真的」變成程式碼改動。

**決定：假的不是我們的 adapter，假的是綠界的伺服器。**
`src/Tools/GreyGray.Tools.EcpaySimulator`（獨立行程）扮演綠界：收結帳表單、驗簽、把付款結果通知 POST 回 `ReturnURL`、
回應退刷 `DoAction`。dev 只靠**本來就可設定**的 `Payment:ECPay:CheckoutUrl`／`CreditDetailUrl` 指過去。
`EcpayGateway`、回呼處理、事件、outbox、Worker、分錄全部照正式碼跑。

**正式碼唯一新增**：`Payment:ECPay:AllowNonEcpayEndpoints`（預設 false）——兩個網址不是 https 的
`ecpay.com.tw`／`*.ecpay.com.tw` 就在 DI 解析期拒絕啟用。正式機忘了拿掉 dev 設定會立刻炸，不會默默打到模擬器。

**換回正式** ＝ 不設那兩個網址（或設正式站）＋ 真憑證 ＋ 不開旗標。**沒有任何一行程式碼要改**——
這就是「可以更換 adapter 就可以上了」的具體形狀。

**附帶**：
- 模擬器只接受 `DEVFAKE` 開頭的 MerchantId；它發出的 `TradeNo` 以 `DEVFAKE` 開頭，進了 `ProviderTransactionId` 之後在後台與 DB 一眼看得出是模擬的。
- 模擬器不送 `SimulatePaid=1`，`AllowSimulatedPaid` 維持 false——回呼走的是**正式**那條判斷。
- 順帶修 #33（付款完成後沒有路回商店）：簽章加 `ClientBackURL`，由 Host 用 `Storefront:PublicOrigin` 組出 `/payment/result?orderId=`。
  不做 `OrderResultURL`（要多一個接受瀏覽器 POST 的端點，留追蹤項）。

---

## ADR-030　結帳的 `shippingPolicy` 只在混合購物車必填；單一模式由後端推導
**狀態**：已採納（2026-09-02，使用者問「哪一種是治本的方式」後拍板「那就用第二種方式修」）

**問題**（#37）：契約 `POST /v1/cart/checkout` 的說明文字寫「**混合訂單**必須指定 `shippingPolicy`」，schema 卻把它列成**一律必填、不可為 null**；
後端輸入型別是不可為 null 的 enum；前端照說明文字做（只有混合才問、否則送 `null`）。結果：**只有現貨或只有預購的購物車一律結帳 500**，
而且炸在 request body 綁定期，比登入檢查還早——沒登入的客人連「請先登入」都看不到。同一條規則有四份、互相打架。

**否決**：「前端補預設值」——快，但把領域規則抄進每一個客戶端（將來 App、LINE 下單都要再抄一次），資料庫裡每張單一模式訂單都存一個客戶端編出來的值，
而契約仍然自相矛盾。

**決定**：規則的主人是後端（`hasMixedModes` 本來就是後端算的）。
- 契約：`shippingPolicy` 改為「`Cart.hasMixedModes = true` 時必填，否則可省略或 `null`」——這是把 schema 改成說明文字早就在說的意思，
  **向下相容**（原本有帶值的客戶端照樣合法）。`Order.shippingPolicy` 維持必填：訂單永遠帶一個值。
- 後端：混合卻沒帶 → `422 checkout.shipping-policy-required`；單一模式 → 忽略客人送的值，依 line 組成推導一個**如實描述會發生什麼**的值：
  純現貨 → `ShipSeparately`（現貨先出）、純預購 → `HoldUntilComplete`（等回國一起出）。`ShippingPolicy` 在 `src/` 沒有任何行為分支，推導不改變出貨行為。
- 前端：重生型別後拿掉 `!`；它現在送 `null` 的行為反而是對的。

**附帶**：壞掉的 request body（JSON 解析失敗、enum 值不合法）在 Development 是 500、在 Production 是空 body 的 400——
兩者都改成 `400` ＋ `application/problem+json`（`platform.malformed-request`），`docs/05` 早就規定錯誤不會是 500。

---

## ADR-031　正式機拓樸：前台網頁與它的 API 同一個主機名稱、`/v1/*` 分流；網域 `greygray.shop`（前台根網域、後台 `admin.greygray.shop`）
**狀態**：已採納（2026-09-02，使用者拍板：串綠界要「佈署到正式機」；網域 `greygray.shop` 在 Cloudflare Registrar 買的、DNS 已在 Cloudflare；前台用根網域、後台 `admin.`；憑證先用綠界公開測試商店；通道另起本機管理的 `GreyGray-Tunnel`，現有 token 式通道不動）

**問題**（E2，BE-28／FE-12 起就記著）：`gg_session`／`gg_cart` 是 host-only cookie（沒有 `Domain` 屬性）。前台頁面伺服器（Next SSR／middleware）要看得到登入 cookie，
就必須跟發 cookie 的 BFF **落在完全相同的主機名稱**——契約 `servers` 原本寫的 `api.greygray.tw`（API）與前台分開的做法，SSR 永遠看不到 cookie。
另外綠界回呼 `ReturnURL` 必須對外可達、只准 80／443。

**決定**：一個主機名稱給一個 app，用路徑分流：
- `greygray.shop`：`/v1/*` → 本機 `5000`（Storefront Host），其餘 → `5002`（Next storefront）。
- `admin.greygray.shop`：`/v1/*` → 本機 `5001`（Admin Host），其餘 → `5003`（Next admin）。
- 綠界 `ReturnURL` ＝ `https://greygray.shop/v1/webhooks/ecpay`；`ClientBackURL` ＝ `https://greygray.shop/payment/result?orderId=…`。
- 同一個 origin ⇒ 前台對 BFF 的呼叫不是跨源，**CORS 整個不需要**（Production 不設 `Cors:AllowedOrigins`）。
- 後端新增選填設定 `Storefront:PublicApiOrigin`（BE-42）：有設就用它組 `ReturnURL`，沒設維持用請求的 scheme/host（dev 模擬器）。
  正式機由 `deploy.ps1` 強制投遞 `Storefront__PublicOrigin` 與 `Storefront__PublicApiOrigin`（兩者在這個拓樸下同值）。
- 前端 artifact 的 API base 在建置期決定（`NEXT_PUBLIC_API_BASE_URL`），前台建 `https://greygray.shop`、後台建 `https://admin.greygray.shop`。

**否決**：維持 `api.greygray.tw` 分開——要改 cookie `Domain` 屬性與 middleware，多一個後端包，而且把 session cookie 放大到整個網域。

**附帶**：契約 `servers` 裡的 `api.greygray.tw`／`admin.greygray.tw` 是舊寫法，隨部署文件一起改成上面兩個主機名稱。
通道用哪一種（另起本機管理的 tunnel，或在現有 token 式通道的儀表板加規則）不影響這個 ADR，兩者都做得到同一張路由表。

## ADR-032　修訂凍結契約：新增 `POST /v1/products/{productId}/skus`（建立 SKU）；後台補「新增 SKU」與「批號進貨」
**狀態**：已採納（2026-09-03，使用者在正式機後台建商品後卡在「這個商品還沒有 SKU」，拍板「不種，等正式做法」）

**問題**：凍結的 admin 契約只有 `PATCH /v1/skus/{skuId}`（修改既有 SKU），`AdminProductInput` 不含 SKU；M1a 時的決定是「Host 不可自行新增未凍結的路由」，Catalog 模組只在 input port 提供 `CreateSkuAsync`（見知識庫「M1a Catalog frozen API 缺少 SKU 建立入口」）。
結果：**正式機沒有任何合法路徑建出第一個 SKU**——沒有 SKU 就沒有價格、庫存、加入購物車。開發機那五個商品的 SKU 是 Leader 直接寫資料庫種的，正式機不能這樣做。
同型的洞還有一個：M2 的 `POST /v1/lots`（批發進貨）早就在契約裡，但後台沒有任何進貨頁，庫存同樣進不來。

**決定**：正式修訂凍結契約，**純新增**一條 operation：
- `POST /v1/products/{productId}/skus`：tag `catalog`、`x-required-role: Operator`、`Idempotency-Key`、request `AdminSkuInput`、`201 AdminSku`、`403`／`404`（商品不存在）／`422`；里程碑 M1a（`docs/05` 表跟著加列，`check-openapi` 的里程碑清單跟著更新）。
- 既有 operation 與 schema **零改動**；`AdminProductInput` 維持不含 SKU（商品仍可先建為空 SKU 集）。
- 後台：商品頁 SKU 區加「新增 SKU」（重用既有的 SKU 編輯抽屜）；SKU 列加「進貨」（`POST /v1/lots`）與批號列表（`GET /v1/lots?skuId=`）。

**否決**：直寫資料庫種 SKU（使用者否決；正式機不留這種路）；把 SKU 陣列塞進 `AdminProductInput`（動到既有 schema 與「修改商品」的語意）；捏造預設重量／尺寸（沿用 M1a 的紀律）。

**附帶**：這是第一次修訂凍結契約的 operation 集合。規則寫死：**只准純新增**、`docs/api` 與 `docs/05` 同一個 commit、`ops/check-openapi.ps1` 在同一包內轉綠、前端下一包 `pnpm api:generate` 重生型別（不准手寫請求型別）。

## ADR-033　前端「不做金額運算」的唯一例外：商品頁小計預覽（單價 × 數量，只用於顯示）
**狀態**：已採納（2026-09-03，使用者在正式站用綠界測試站付款走通後回報 #40，拍板「前端乘，當小計預覽」）

**問題**：商品頁底部列只顯示單價，使用者選了 5 件卻看不到總額；商品資訊區也沒有單價。這一頁還沒進購物車，後端沒有任何端點能回「這個數量的小計」；
而前端四條之二是「不做金額運算——加總、分攤、含運總額由後端回」（`frontend/README.md`）。

**決定**：允許、而且只允許這一個例外——商品頁底部列顯示「N 件 · 小計 NT$X」，X ＝ 選定 SKU 的 `price.amountMinor × quantity`：
同幣別、整數乘法、只在瀏覽器顯示、不送後端、不存任何地方；字面標「小計」，實際金額一律以購物車（後端 `GET /v1/cart` 回的小計／含運）為準。
單價（含既有的 `unitPriceLabel`）同時顯示在商品資訊區，讓「單價、數量、小計」三個數字在同一頁都看得到。

**否決**：後端加「試算」端點（多一條契約、每改一次數量打一次）；只顯示「單價 × 數量」不給總額（使用者要看到數字）。

**附帶**：計算放在單一純函式（`subtotalPreview`）並附測試；例外註記寫在 `frontend/README.md` 第 2 條旁邊；購物車、結帳、訂單頁**不准**照抄——那些頁面的金額仍全部來自後端。

---

## ADR-034　架構準則的界線：SOLID 與設計模式是審查準則；Clean Architecture 不構成重構授權
**狀態**：已採納（2026-09-06，使用者提出六條開發工作流，Leader 與外部模型 Codex／gpt-6-astra 聯合討論後拍板落點）

**問題**：使用者要求把「Design Pattern 設計、SOLID 原則、Clean Architecture 架構」納入專案規則。
現有規則裡這三個詞零命中（`CLAUDE.md`、`.dispatch/`、`docs/` 全查過）。
直接寫成第七～九條鐵則有兩個具體風險：① 六條鐵則現在每一條都能一眼判定違反（金額不是 `Money`、
模組參考到 `*.Core`），SOLID 判不了，混進去會稀釋鐵則的效力；
② 「必須遵守 Clean Architecture」會被子代理正當地解釋成「拆 Domain／Application 專案、
搬移型別、加轉換層」的授權——每一步都能自圓其說，卻沒有任何已批准的業務收益，
而現在是 315 條後端測試、剛在正式機走通全流程的狀態。

**決定**：
1. **六條鐵則不動。** SOLID 與設計模式寫成**審查準則**，放 `docs/45-開發工作流與設計準則.md` 第一節：
   優先沿用既有模式（`Result`、Outbox、Saga timer、Aggregate、Contracts-only、`IClock`）；
   新增抽象要寫得出「解決的具體問題／更簡單的做法為何不夠／維護代價」三件事，寫不出就不加。
2. **Clean Architecture 寫成現況的界線，不是目標狀態**：本專案維持模組化單體與各模組
   `Core`／`Infra`／`Contracts` 分層，依賴方向朝核心，跨模組只經 `Contracts`，以架構測試為準。
   **不承諾任何特定樣板**（四層命名、Ports & Adapters 目錄形狀），
   且**本條不構成重構授權**——不得以「改成 Clean Architecture」為理由提重構案。

**否決**：直接把三個名詞加成鐵則（判不了、且發出重構授權）；
把 SOLID 寫成 architecture test（五項原則裡只有 DIP／ISP 的一部分機械查得出來，
其餘會變成「查了零個對象但看起來全綠」——這正是 `selftest.sh` 第四次稽核抓到的那一族病）。

**附帶**：架構或契約要變，仍走 ADR ＋ 派工，不因本條放寬。

---

## ADR-035　跨平台／手機：保留可能性，現在不動工；原生 App 與現行 cookie-session 契約有實質衝突，要另外拍板
**狀態**：已採納（2026-09-06，使用者提「後續可跨平台使用，我也想界接到手機」）

**問題**：現有文件只有「手機優先」的響應式網頁（`docs/00-decisions.md` 前端那一段、`docs/06-前端工作包.md`），
沒有原生 App 或 API-for-mobile 的決策。而 `docs/05-API契約.md` §2 已經凍結：
**access token 永遠不進瀏覽器，前端只拿 HttpOnly cookie，BFF 自己保管 session，
「任何要求前端存 token 的設計都是錯的」**。原生 App 不是瀏覽器，這一條對它不是自動成立的。

**決定**：
1. **現在不動工。** 後端已是契約凍結的 REST ＋ 前端經 BFF，這已經是保留可能性的最低成本形式。
2. **現在就要守的**：業務規則留在後端；不新增「只有 Next.js 頁面才成立」的業務流程；
   維持錯誤語意與契約相容性。
3. **現在不要做**：選框架、開 App 專案、拆微服務、重寫登入，或為了「將來手機要用」放寬 API 暴露與 CORS。
4. **要拍板才動的分岔**（先講清楚，避免將來被當成實作細節順手決定）：
   App 要嘛當成另一個 BFF 客戶端、自己管 cookie jar（不改契約），
   要嘛發 token 給原生端（**直接牴觸 §2**，要改凍結契約）。
   另外還要一起決定：付款返回／深連結、推播、檔案上傳、離線這四項的邊界。
   在拍板之前，「接手機」不是可以寫進派工書的需求。

**否決**：現在就發 token 端點「先做著等以後用」（改凍結契約、擴大攻擊面，而且沒有任何客戶端在等它）；
把「手機」寫成一條原則（原則管不住上面那個分岔，該產出的是這則 ADR）。

**附帶**：`docs/45-開發工作流與設計準則.md` 第三節有一句話版本，派工書引用那一節即可。

---

## ADR-036　最愛清單：修訂凍結契約，純新增三條 operation；資料放 `catalog` schema；只給登入客戶
**狀態**：已採納（2026-09-15，使用者以 `/goal` 下達「不是購物車而是最愛清單——可以加上我的最愛瀏覽」，並授權由 ai-cli 團隊處理；細節由 Leader 定案）

**問題**：契約從 M1a 起就有 `ProductListItem.isFavorited` 與 `ProductDetail.isFavorited`，**但沒有任何寫入或列出的端點**。
後端把它寫死成 `false`（`M1aEndpoints.cs` 的 `ToProductListItem`／`ToProductDetailAsync`）；前台的愛心是元件本地狀態，
**按了會變色、重新整理就消失**（`ProductCardLink.tsx` 的註解自己寫著「純前端本地狀態」）。
這是本專案一再出現的「畫面宣稱了不成立的事」：客人以為收藏了，其實什麼都沒存。

**決定**：
- **契約純新增**（規則比照 ADR-032：只准新增、`docs/api` 與 `docs/05` 同一個 commit、後端包內 `check-openapi` 轉綠、前端重生型別不准手寫）：
  - `GET /v1/me/favorites`：cursor／limit 分頁，回 `{ items: ProductListItem[], nextCursor }`，**最新收藏的在前**。
  - `PUT /v1/me/favorites/{productId}`：`Idempotency-Key`，`204`；**冪等**，已在最愛裡再加一次也是 `204`。
  - `DELETE /v1/me/favorites/{productId}`：`Idempotency-Key`，`204`；**冪等**，本來就不在最愛裡也是 `204`。
  - 三條都要求登入（`401`）；里程碑 M1a（開張前要有）。既有 operation 與 schema 零改動，`isFavorited` 只補說明文字。
- **看得到、收得到的判定與 `GET /v1/products/{productId}` 同一套**：商品上架，**而且至少有一個上架中的規格**。
  那支回 `404` 的，`PUT` 也回 `404`；`GET /v1/me/favorites` 也用同一套，**在分頁之前過濾**（不是撈出來再藏，否則一頁會半空）。
  所以清單裡可能出現「還沒定價」的商品（`priceFrom` 為 `null`），前端要能顯示也要能取消收藏。
  收藏之後不再符合（商品下架、或規格全部下架）：**不刪收藏紀錄**，清單只是不列出它；恢復之後就回來。
  （2026-09-15 覆驗更正：初稿只寫「商品上架」，比既有前台規則寬——Codex 覆驗指出詳情頁對「零個上架規格」也回 not-found。）
- **資料放 `catalog` schema**（新表）。理由：列出最愛要帶商品名稱、圖片、起價，放在 Catalog 同一個 schema 查得完；
  放在 Identity（跟收件地址一起）就得跨 schema JOIN（鐵則 4）或逐筆查（N+1）。
  客戶識別用 `Identity.Contracts` 的 `CustomerId`——Contracts 之間允許相依且必須是 DAG（ADR-014），Checkout／Ordering 等九個 Contracts 已經這樣引用。
- **只給登入客戶。** 未登入按愛心 → 前端導去登入並帶 `next` 回原頁。

**否決**：訪客用 `localStorage` 先收藏、登入後合併（多一套合併規則，而且本地暫存正是現在要拿掉的假狀態）；
放 Identity schema（跨 schema）；設收藏上限（沒有需求；重複由主鍵擋）。

---

## ADR-037　「立即購買」在前端完成，契約零改動；購物車裡還有別的東西時先帶去購物車
**狀態**：已採納（2026-09-15，使用者以 `/goal` 下達「可以新增立即購買」；細節由 Leader 定案）

**問題**：商品頁只有「加入購物車」，買一件東西要走「加入 → 購物車 → 結帳」三頁。

**決定**：
- 「立即購買」用**同一支** `POST /v1/cart/lines` 把目前選的規格與數量加進購物車，成功之後：
  - 購物車裡**只有這一次加入的東西**（只有一行，而且那一行的數量等於這次選的數量）→ 直接到 `/checkout`；
  - 否則（車裡原本就有別的品項，或同一個 SKU 被後端併進既有那一行）→ 到 `/cart`，並提示
    「購物車裡還有其他商品，或這件商品原本就在購物車裡，請確認品項與數量後再結帳」。
    這也涵蓋「按了立即購買、沒結帳就回來再按一次」——同一件商品的數量會疊加，帶去購物車讓客人看得到（Gemini 覆驗指出）。
  - 兩顆按鈕共用一把**同步鎖**（在任何 `await` 之前鎖住），連點或搶按只送一次，**第一次點擊的意圖勝出**。

---

## ADR-038　超商取貨門市用綠界「7-ELEVEN 電子地圖」選；後端發一次性選店票、驗回傳、把門市名稱與地址凍結進訂單
**狀態**：已採納（2026-09-15，使用者以 `/goal` 問「可以新增 7-11 收貨嗎」；規格由 Codex `gpt-5.6-sol` 與 Gemini `3.1-pro-high` 交叉查證，細節由 Leader 定案。**正式物流開通要老闆向綠界申請**）

**問題**：契約從 M1a 起就有 `DeliveryMethod.ConvenienceStore`、`CheckoutRequest.convenienceStoreCode`（說明寫「綠界電子地圖回傳的門市代號」）與 `Order.convenienceStoreName`，
但**從來沒有接電子地圖**：前台 `ConvenienceStoreField.tsx` 是手動輸入代號（檔頭自己寫「假設，未接真正的綠界電子地圖」），
後端 `OrderResponse` 的「門市名稱」位置塞的是**門市代號**，後台契約完全沒有門市欄位——出貨的人看不到要寄到哪一家。

**查證過的外部事實**（官方文件 developers.ecpay.com.tw 8795／10087／10092、support.ecpay.com.tw 21685）：
- 地圖 `POST https://logistics(-stage).ecpay.com.tw/Express/map`，form-urlencoded：`MerchantID`、`LogisticsType=CVS`、`LogisticsSubType`、`IsCollection`、`ServerReplyURL`，選填 `MerchantTradeNo`、`ExtraData`（≤20）、`Device`。
- **地圖請求不需要 CheckMacValue，回傳也沒有** → 回傳內容可被偽造。
- 客人選完門市後，綠界**經客人的瀏覽器**把 `CVSStoreID`、`CVSStoreName`、`CVSAddress`、`CVSOutSide`、`ExtraData` 等 POST 到 `ServerReplyURL`（那一頁就是客人看到的頁）；`ServerReplyURL` 只接受 80／443。
- 手機不可放 iframe、iOS 不要 `target=_blank`，同頁頂層導向最穩。
- 小量、自己拿去門市寄 → 綠界建議 **C2C（`UNIMARTC2C`）**；B2C 要測標。金流開通不等於物流開通。
- 建物流單（`Express/Create`，要 CheckMacValue）**不是**選門市的前提。

**決定**：
1. **C2C `UNIMARTC2C`、`IsCollection=N`**（付款仍走 AIO）。物流設定與金流**分開管**：`Logistics:ECPay:MerchantId`、`Logistics:ECPay:LogisticsSubType`、`Logistics:ECPay:MapUrl`；
   網址守衛比照 ADR-029（`*.ecpay.com.tw` 以外要明確開 `Logistics:ECPay:AllowNonEcpayEndpoints`）。**缺設定不擋開機**，選門市端點回明確的 `logistics.not-configured`——正式機目前沒有物流帳號，不能因為這一波讓整站開不了機。
2. **流程**（契約純新增）：
   - 結帳頁按「選擇門市」→ `POST /v1/logistics/cvs-map-sessions` → 後端產生**一次性選店票**（密碼學亂數、≤20 字、存 Garnet、綁購物車 cookie、15 分鐘過期）→ 回 `{ action, method, fields }`；
   - 前端照 `/payment/[orderId]` 的做法用隱藏表單**頂層自動送出**到綠界地圖；
   - 綠界經瀏覽器 POST 到 `{Storefront:PublicApiOrigin}/v1/logistics/cvs-map/reply`（**不是給前端呼叫的**）→ 後端驗：票存在、未過期、未用過、`MerchantID` 與 `LogisticsSubType` 相符、門市代號與名稱地址的格式與長度 → 把門市存回票上 → **303** 導回固定的 `{Storefront:PublicOrigin}/checkout?cvsSelection=<票>`（名稱地址不放網址）；驗不過也 303 回結帳頁並帶錯誤代碼，**不給客人看 500**；
   - 結帳頁 `GET /v1/logistics/cvs-selections/{票}`（同一台購物車才看得到）顯示門市名稱、地址；
   - 送出訂單帶新的選填欄位 `convenienceStoreSelectionId` → 後端從票取出代號、名稱、地址**凍結進訂單**。舊的 `convenienceStoreCode` 保留向下相容。
3. **訂單上看得到門市**：前台 `Order.convenienceStoreName` 改成真的名稱（修掉塞代號的錯位）、純新增 `convenienceStoreAddress`；後台訂單契約純新增門市代號、名稱、地址——出貨的人要知道寄到哪。
4. **dev**：綠界模擬器（ADR-029 那一支）加 `/Express/map` 假地圖，列幾家假門市、以瀏覽器表單 POST 回 `ServerReplyURL`，行為跟真綠界同形。
5. **不在這一次**：`Express/Create` 建物流單與交貨便代碼（出貨時由營運在綠界後台處理）、門市清單 API 複核、全家／萊爾富。
   **已知缺口（Gemini 情境覆驗提出，Leader 確認）**：超商取貨寄件要**收件人真實姓名與手機**，但結帳不收、後台訂單只看得到遮罩後的聯絡方式（明文要走 ADR-017 的稽核，而稽核寫入 BE-50 還沒生效）。
   宅配的收件資訊在後台一樣看不到，是同一個缺口；牽涉個資留存與稽核，**要使用者拍板**後另開一包，不併進這一波。
   LINE／Facebook 內建瀏覽器若把地圖開到外部瀏覽器，回程 cookie 不共用、會回到空購物車——上線後觀察，必要時在結帳頁提示改用外部瀏覽器。

**否決**：只在前端換成地圖、後端照收客人送來的代號與名稱（回傳沒有檢查碼，名稱地址由客人任意填，進了訂單就成了營運的寄件依據）；
把門市資料塞在導回網址的 query 裡（會被分享、被竄改、進瀏覽紀錄）；缺物流設定就擋開機（正式機現在沒有物流帳號，會把整站拖垮）；B2C（要測標，不符合現在自己拿去門市寄的出貨方式）。

**附帶**：偽造的門市代號只影響偽造者自己那一張單（寄不出去由營運發現），所以這一次不做門市清單複核，列為後續強化。
正式上線前 Leader 要在正式機放物流設定；在老闆申請到正式 C2C 物流帳號之前，內測可以先用綠界公開的 C2C 測試帳號。
- 能不能按的條件與「加入購物車」**完全相同**（同一個 disabled 判斷，不另寫一份）。
- 未登入：沿用既有流程——`/checkout` 撞 `401` 會帶 `next` 去登入再回來（FE-25／FE-26 已做好）。

**否決**：後端「只結帳單一品項」或另開一台 buy-now 專用購物車（要改結帳契約與 BE-48 的「結帳成功就讓購物車退休」規則，多一套購物車生命週期）；
一律直接進 `/checkout`（車裡有舊品項時會一起結帳，客人以為只買這一件——那是宣稱不成立的事）。

---

## ADR-039　訂單收件人資訊：下單時凍結快照、後台全員看明文、不加解鎖閘門
**狀態**：已採納（2026-09-19）

**背景**：超商取貨與宅配都需要收件人真實姓名與手機才寄得出去，但現況有三個洞：

1. 後台**什麼都看不到**——`GreyGray.Api.Admin/M1aEndpoints.cs` 把 `CustomerContactMasked` 寫死成 `null`，
   `AdminOrderResponse` 連 `ShippingAddressId` 都沒有。
2. **#56：宅配收件資訊沒有凍結快照**。訂單只存 `ShippingAddressId`，前台是即時回查地址簿；
   客人事後改地址 → 舊訂單跟著變，刪地址 → 舊訂單的 `shippingAddress` 直接變 `null`。
   ADR-038 已經替超商門市做了凍結，宅配這半沒人做。
3. 超商取貨根本沒有收件人欄位。

**決定**：

- 收件人姓名與手機**在下單當下凍結進訂單**：宅配從地址簿抄一份，超商由客人自己填
  （地址簿的收件人不見得是去超商領貨的人）。凍結路徑照抄 ADR-038 的五段。
- **宅配地址也一起凍結**（`recipientAddress`，完整單行字串，2026-09-19 驗收時補進本波）。
  後台在這之前**完全沒有任何地址欄位**，只有姓名電話與超商門市——宅配一樣寄不出去，
  這個洞由 BE-54 的自驗報告挖出來。刻意存單行字串而不拆結構化欄位：
  後台出貨只需要「印出來貼在包裹上」的地址，拆開反而要在前端再拼一次。
  前台維持結構化的 `shippingAddress`（欄位分開顯示），兩者用途不同。
- 後台**所有員工**直接看得到明文，**不遮罩、不加解鎖按鈕、不要求填存取理由**。
- `AdminOrder.customerContactMasked` 保留欄位（相容）但恆為 `null`，
  舊描述承諾的「另外呼叫並填寫存取理由」端點**從來沒有實作過**，描述一併改寫。

**理由**：出貨是全體營運的日常工作，遮罩＋解鎖會讓每次出貨多一道摩擦，
卻換不到實際保護——後台帳號本來就看得到訂單全貌。使用者 2026-09-19 拍板。

**取捨與失效條件**：

- 快照**存明文，不加密**。理由：後台全員可見，加密只擋得住直接讀資料庫的人；
  而同一份資料會隨 `CheckoutCompleted` 進到 `platform.outbox_message` 的 payload，
  **只加密 `orders` 而不加密 outbox 是自欺**。
- ⚠ **失效條件**：一旦後台開始有「外部合作夥伴帳號」，或員工數量成長到看不住，這個決定要重審。
  Leader 在提出時已向使用者說明「不遮罩＝任何有後台帳號的人都看得到全部客人個資，帳號外流即整批外洩」，
  使用者維持原決定。
- Identity 地址簿**維持 AES-256-GCM 加密不動**（它的存取路徑不一樣）。
- **#57：`platform.outbox_message` 永不清理**（`OutboxDispatcher` 投遞後只標 `ProcessedAt`，
  全 `Platform/Outbox` 沒有任何刪除或保存期限）。把個資放進事件 payload 會讓它永久留存，
  **所以保存期限是這個決定的前提條件**，與本 ADR 同一包做掉；`IsDeadLettered` 的不刪。
- 冪等指紋加入新欄位後，同一把 `Idempotency-Key` 換掉收件人會回 `422 platform.idempotency-key-reused`，
  而不是像現況那樣回快取的舊回應、把新收件人靜靜吞掉。**這個行為改變是要的。**

**否決**：

- 只給出貨角色看＋每次讀取寫稽核（使用者明確不要這道摩擦；而且 `IAuditWriter` 至今沒有任何實作，
  BE-50 未生效，寫了也寫不進去）
- 加密快照（擋不住真正的洩漏路徑，卻讓後台查詢變複雜）
- 不做快照、繼續即時回查地址簿（#56 就是這樣來的）
- 宅配也要客人在結帳頁重填一次姓名手機（兩個地方各填一次一定會不一致）

---

## ADR-040　客服工單自建模組 `CustomerService`，不接 AI、不開 GitHub issue
**狀態**：已採納（2026-09-19）

**背景**：FAQ 有三處寫「請聯絡客服」，但站上**沒有任何客服管道**。

**決定**：

- 前台右下角做**展開式客服小視窗**，走**引導式選單**：常見問題的選項與答案
  **直接取自 FAQ 的 `FAQ_GROUPS`**（不另抄一份會走鐘的文案），選不到答案才進留言。
- 留言進**站內自己的工單列表**，後台新增一頁「客服訊息」。
- 新開模組，**取名 `CustomerService` 而不是 `Support`**：
  架構測試的 `SupportModules` 指的是「**支撐模組**：不被業務模組依賴、只訂閱事件」，
  而客服工單要被兩個 Host 直接呼叫，不屬於那一類。名字撞在一起會讓每個讀架構測試的人重新判斷一次。
  **對外端點路徑仍用 `/v1/support/tickets`**（對客人而言比較短而自然），這個不一致是刻意的。
- 留言**匿名可打**（訪客也會有問題），但 email 與手機至少要留一個，否則無從回覆。
- **防灌**：同一 IP／同一購物車 cookie 每小時上限，用既有的 `IDistributedCache`（Garnet）計數。
  ⚠ **KV 掛掉時 fail-open（放行）並記 error log**——客服留言不是安全邊界，
  寧可收到灌水，也不要讓真客人問不了問題（何況 session 也靠 Garnet，它掛掉時整站登入已經先壞了）。
  底層是 `AddStackExchangeRedisCache`，斷線會**拋例外**，所以 fail-open 要自己包 try-catch，不會自動發生。

**理由**：客人的姓名與訂單內容不該離開自己的系統（GitHub issue 是公開的、或需要外部帳號，
還要把 token 放正式機）；引導式選單不會亂承諾退款，也不花 API 費用、不用把金鑰放正式機。
使用者 2026-09-19 拍板。

**否決**：開成 GitHub issue；只寄 Email 給客服信箱（沒有列表可以追蹤處理狀態）；
接 AI 自動回答（會亂承諾退款；成本與金鑰都要另外管）；前台做「查詢我的工單」頁
（匿名工單沒有安全的查詢方式，做了就是給人猜別人的工單編號）。

---

## ADR-041　商品分類固定兩層；`parentId` 與 `includeDescendants` 純新增；前台分類清單的發布條件改為「自己或直接子分類有可售商品」（ADR-032 的明文例外）
**狀態**：已採納（2026-09-30，團隊回饋「漢堡選單展開後的分類層級分明」；使用者拍板「前台手機加漢堡選單，而且商品分類改成多層」，接受修訂凍結契約；細節由 Leader 定案，計畫見後端樹 `docs/58-第四十二波計畫書.md`）

**問題**：`catalog.category` 只有 `id/tenant_id/name/image_url/sort_order`（`0006`），沒有父子關係。
前台 `GET /v1/categories` 只列「自己**直接**有可售商品」的分類，`GET /v1/products?categoryId=` 是相等比對。
只加父子欄位、不動清單條件的話，**只有子分類有商品的父分類會從清單消失**，前台組不出樹。

**決定**：

- **固定兩層。** `parentId` 只能指向根分類（`parentId` 為 `null` 的分類）；已經有子分類的分類不能再被設成別人的子分類；不能指向自己。
  違反時回 `422`，兩個錯誤碼：
  - `catalog.invalid-parent-category`：上層分類不存在、不屬於同一個租戶、或指向自己
  - `catalog.category-depth-exceeded`：上層不是根分類，或自己已經有子分類
  錯誤碼**刻意不含 `not-found`**：`BffHttp.StatusFor` 看到這個子字串會回 `404`，而 PATCH 的 `404` 意思是「路徑上的分類不存在」，兩件事不能混在一起。
- **商品可以直接掛在父分類上**（營運初期不必為了一兩個商品硬開子分類）。
- **資料庫也守這條規則。** `0023` 加 `parent_id`、同租戶複合外鍵、`CHECK (parent_id <> id)`，
  外加一個 trigger：寫入 `parent_id` 時再驗一次兩層規則，並且先拿「每個租戶一把、交易層級」的 advisory lock，
  讓同一個租戶的分類階層寫入排成一列。
  理由：應用層檢查擋得住一般操作，**擋不住兩個人同時把 A 設成 B 的子、把 B 設成 A 的子**（兩邊讀到的對方都還是根分類），
  也擋不住直接寫資料庫的種資料。分類寫入很少，排成一列的代價可以忽略。
  一般的違規由應用層先擋、回 `422`；萬一是競態被資料庫擋下（SqlState `23514`），Catalog 的 Infra 把它轉成同樣的兩個錯誤碼，客人看到的一樣是 `422`，不是 `500`。
  死結（`40P01`）這類真正的意外才走例外（鐵則 5）——應用一次只寫一筆分類，列鎖與 advisory lock 的取得順序固定，正常操作碰不到。
  只改 `tenant_id` 不必觸發檢查：`id` 是全域主鍵，換了租戶的分類一定對不上同租戶複合外鍵（自己的上層、或指向自己的子分類），會先被外鍵擋下。
- **所有讀取路徑都寫成明確的兩層**（根分類 → 直接子分類），不寫遞迴查詢。就算資料被弄壞，也不會無窮迴圈。
- **契約（純新增）**：
  - admin `CategoryInput` 加選填 `parentId`（`Id | null`），`Category` 經 `allOf` 自動帶到。
    **PATCH 本來就是整筆取代**（`imageUrl`、`sortOrder` 省略一樣會被覆寫），`parentId` 同一套：省略或 `null` 就是根分類。
    代價是「只改名稱、忘了帶 `parentId`」會把子分類升成根分類，所以**後台表單（FE-54 起）每次都要送 `parentId`**；不做「省略＝不變」的三態，因為其他欄位都不是這個語意，混用更容易錯。
    POST／PATCH 補列 `422`（既有的 `catalog.invalid-category` 本來就回 `422`，只是契約沒寫）。
  - storefront `Category` 加選填 `parentId`（`Id | null`），後端**照寫不省略**（`docs/05` §6）。
  - storefront `GET /v1/products` 加選填 `includeDescendants`（boolean，預設 `false`）：
    `true` 時 `categoryId` 的比對擴大成「這個分類，或它的直接子分類」；預設值保留舊行為。
    **admin 的 `GET /v1/products` 不加**（後台的分類篩選維持相等比對，需要時另外提）。
- **一條明文例外：storefront `GET /v1/categories` 的發布條件改語意。**
  從「自己直接有可售商品」改成「**自己或任一直接子分類有可售商品**」。
  結果清單**對父分類封閉**：清單裡的子分類，它的父分類一定也在清單裡。「可售」的定義不變（商品上架、至少一個上架中的規格、現貨要有標價）。
  這改的是既有 operation 的語意，**與 ADR-032 定下的「修訂凍結契約只准純新增」衝突**，例外理由：
  0. **不算 `docs/05` §1 定義的破壞性變更，所以不開 `/v2`**：沒有刪欄位、沒有改欄位型別或欄位語意、沒有新增必填欄位；
     回應清單只會變成原本的**超集**（多出「只有子分類有商品的父分類」），原本會出現的分類一個都不會消失。
  1. 平面分類（沒有子分類的）行為完全不變——條件退化成原本那一條。
  2. 行為會變的只有「有子分類的父分類」。而在後台能設定上層分類之前（第四十三波 FE-54），**沒有任何畫面建得出父子關係**；
     API 直打與種資料腳本由 Leader 管控，**BE-58 部署到 FE-54 部署之間不在正式站建父子分類**。
     ⚠ 已知風險：這段期間若在 dev 用 API 建了子分類，再用**現行後台**的分類對話框編輯它，對話框的 PATCH 不帶 `parentId`，子分類會被打平成根分類——
     dev 驗收建好階層之後不要用舊後台去編輯。FE-54 必須做到：對話框一律送 `parentId`（從既有值預填）；mock 的 PATCH 改成整筆取代（現在是合併，會把漏送 `parentId` 的錯誤蓋掉）。
  3. 2026-09-30 盤點前端所有呼叫端：沒有任何程式碼**過濾或計數**依賴「只列直接有商品的分類」。
     會看到差別的畫面有三個，都在 FE-54 接上樹狀資料：首頁 `CategoryRail` 與第四十二波 FE-52 的導覽抽屜會把整份清單攤平列出；
     `/categories/[id]` 用「在不在清單裡」判斷 404，所以「只有子分類有商品的父分類」頁面不會 404，而是顯示「這個分類目前沒有商品」（FE-54 改用 `includeDescendants=true`）。
  4. 替代方案「新增參數（例如 `includeAncestors`）維持純新增」會讓預設行為組不出樹，每個新呼叫端都得記得帶參數，而舊行為沒有任何消費者需要保留。
- `docs/api` 與 `docs/05` 同一個 commit，由 Leader 在派工前寫好、兩棵樹同步；實作包不改契約。
  `check-openapi` 只比 operation，這次 operation 集合沒有變，所以 `parentId` 由實作包的回應 JSON 測試證明，不另外驗 live OpenAPI 產物。前端由 FE-54 跑 `pnpm api:generate`。
- 順帶記下、不在本決定處理：storefront `GET /v1/products` 的錯誤回應（`422 catalog.invalid-category-id`、查詢參數型別不對時的回應）一直沒有寫進契約，另案補。

**否決**：

- 不限層數（遞迴查詢、防循環、前台多層展開都變複雜，營運上看不到需求）
- 用 `includeAncestors` 參數維持純新增（理由見上）
- 只在應用層檢查、不加資料庫守衛（擋不住併發，也擋不住直接寫資料庫）
- 這次一起加 `DELETE /v1/categories/{categoryId}`（另外記下，不在本決定）

---

## ADR-044　綠界非即時付款（ATM／超商代碼／條碼）正式支援；付款通知一律先驗簽再去重、依事件分 scope；做完之前只收信用卡

**狀態**：已採納（2026-09-30，使用者拍板「路線 B」與四項規則；依據 `.dispatch/reports/BE-59.md` 與 `docs/66-第四十三波計畫書.md`）

**問題**：付款頁送 `ChoosePayment=ALL`，綠界會讓客人選 ATM／超商代碼／條碼，但系統只做了「即時付款」這一條路。BE-59 查證（C0～C10）：
取號結果要送到 `PaymentInfoURL`，我們沒送；取號通知若進了服務層會被當成付款失敗；30 分鐘後重開付款會換新單號，客人拿舊帳號／代碼繳費時入帳會丟例外；
20 分鐘的通知時間窗擋掉條碼（官方明載可晚兩天通知）；`Order.PaymentDueAt` 從未被賦值；退款只有信用卡退刷一條路。
另外付款通知端點**在驗簽之前**就用 `MerchantTradeNo` 佔住冪等鍵，偽造一次就能讓那筆訂單真正的付款通知永遠被擋（#64，BE-59 評 High，連信用卡都受影響）。

**決定**：

- **付款通知一律先驗簽、再寫冪等。** 驗 `CheckMacValue` 與 `MerchantID` 的入口放在 `Payment.Contracts`（只驗不寫、不讀資料庫），Host 呼叫它；
  **Host 不准自己重算 `CheckMacValue`**（演算法只准一份）。驗簽失敗直接回 `422`，冪等表零寫入。服務層原本的驗簽保留作第二道防線。
- **冪等 scope 依事件類型分開**：付款結果 `webhook:ecpay:payment-result`，key＝`MerchantTradeNo:TradeNo:RtnCode`（對齊 `docs/05` 的「`MerchantTradeNo`＋交易編號」，再加 `RtnCode`：同一交易編號的失敗與成功、取號與付款各自一把鍵，不會互相擋），
  hash **只算這三個已驗欄位**、不算整份表單（綠界重送同一事件時欄位略有不同，也不會變成「同鍵不同內容」而被永久擋下；2026-09-30 Codex 覆驗提出）；
  取號結果 `webhook:ecpay:payment-info`（BE-63 加）。舊 scope `webhook:ecpay` 停用——被偽造請求佔住的舊列因此自然失效，**不刪正式機資料**。
- **過渡期只收信用卡。** BE-61 起 `ChoosePayment` 寫死為 `Credit`（具名常數，不做設定開關：沒做完本 ADR 的其餘部分就打開，等於重新暴露上述問題）。
  重新開放在最後一包（BE-66）：**客人先在我們網站選付款方式**（信用卡／ATM 轉帳／超商代碼／超商條碼），每筆付款只送**單一** `ChoosePayment`（`Credit`／`ATM`／`CVS`／`BARCODE`），**不用 `ALL`＋`IgnorePayment`**（2026-10-01 使用者拍板，理由見「否決」）。**不能只把字串改回 `ALL`**。
- **繳費期限統一 3 天，主動送給綠界**：依客人選的方式各送各的——ATM `ExpireDate=3`（天）、超商代碼 `StoreExpireDate=4320`（分鐘）、條碼 `StoreExpireDate=3`（天）。
  取號後訂單的 `PaymentDueAt` 以綠界取號通知回傳的 `ExpireDate` 為準（綠界期限＝訂單期限）。**ATM 的 `ExpireDate` 只有日期（`yyyy/MM/dd`），換算成當天 23:59:59（台北時間）**；
  超商代碼／條碼帶完整時間。一律先以台北時區解讀、再轉 UTC 存（#35 的教訓：帶 +08:00 偏移寫 `timestamptz` 會炸）。
- **所有訂單建單時 `PaymentDueAt`＝建單＋24 小時**；取號成功改成綠界期限。逾期未付自動取消、釋放庫存，沿用鑑賞期那套 saga 計時器。
- **逾期取消與入帳的競態**：Order 補併發控制（樂觀鎖）。**取消之後才入帳不丟例外**：付款照樣記成已收款，訂單維持已取消並標記待退款——
  信用卡走既有退刷，非信用卡轉「待人工退款」。
- **非信用卡退款＝人工匯款、後台登記**：客服向客人要帳戶後自行匯款，再到後台登記已退金額。**系統不存客人銀行帳號**。
- **Payment 新增「已取號待繳費」狀態 `InstructionsIssued`（值 5）**：取號（ATM `RtnCode=2`、超商代碼／條碼 `RtnCode=10100073`）不再當失敗；已取號的付款不開新單號——
  再發動付款回 `409 payment.instructions-already-issued`，前端改讀訂單的 `paymentInstructions`（不把取號資訊塞進 `PaymentInitiation` 的 200 回應，那會變成破壞性變更）。
  取號資訊存在 Payment（方式、銀行代碼、虛擬帳號、繳費代碼、條碼三段、綠界期限、取號時間）；「取號資訊還有沒有效」由 Payment 的查詢決定（取號狀態與期限）；「訂單層看不看得到」由 Host 依訂單狀態決定——只有待付款的訂單才帶，已取消、已付款一律 `null`（Payment 沒有訂閱 `OrderCancelled`，客人取消後付款仍可能停在已取號；2026-10-01 Codex 覆驗提出）。
  **取號通知另開端點** `POST /v1/webhooks/ecpay/payment-info`（scope `webhook:ecpay:payment-info`，同樣先驗簽、key 同上），由路由決定事件類型、不靠 `RtnCode` 猜。
  取號通知與非即時付款的付款結果通知**不套 20 分鐘時間窗**（取號通知沒有 `PaymentDate`，`TradeDate` 是訂單成立時間；條碼付款通知官方明載可晚兩天），改由驗簽＋狀態機＋冪等防重放。
- **契約純新增**（兩棵樹同步）：第四十四波（BE-62 派工前）寫了 `Order.paymentInstructions`（可為 `null`）與後台 `PaymentSummary` 的付款方式與取號欄位；**待人工退款與登記操作、取號通知端點、期限與取消資訊在第四十五波閘門補上**（2026-10-01 更正：這裡原本寫「BE-62 派工前已含待人工退款標記與登記操作」，當時契約其實沒有）。

**否決**：只把 `ALL` 打開（BE-59 C1～C10 的問題全部會發生）；Host 端自己算簽章（兩份演算法會漂移）；用 migration 刪正式機的冪等列（換 scope 就能解除封鎖，刪了反而失去偽造證據）；
讓客人在前台填退款帳戶（多存一種個資，隱私權政策要補揭露；人工匯款在營運初期量少，成本可接受）；非即時付款用綠界預設期限（超商 7 天，庫存被佔太久）；
重新開放時用 `ChoosePayment=ALL`＋`IgnorePayment`（2026-10-01 查證：超商代碼與條碼在 `ALL` 下共用同一個 `StoreExpireDate`，單位卻一個是分鐘、一個是天，同一筆送不出兩個期限；
`IgnorePayment` 的可用值在官方兩個版本的文件不一致；綠界官方也建議固定指定付款方式）。

**補記（2026-10-01，第四十五波；使用者逐項拍板，依據 `docs/72-第四十五波計畫書.md` 第二節）**：

- **非信用卡逾期多等 2 天才取消**：ATM／超商代碼／條碼過了綠界期限後，系統再等 2 天（吸收晚到的付款通知；條碼官方明載可晚兩天）才自動取消；信用卡建單 24 小時準時取消。
  **客人看到的期限不變**（`paymentDueAt`＝綠界期限）。實際自動取消時點另存在 Order（內部欄位、不進契約）——只存 `PaymentDueAt` 的話，舊的 24 小時計時器觸發時分不出要不要加寬限。
- **寬限期間顯示「確認中」、不能再付款**：`Order.paymentOverdue=true`，前台不顯示付款入口；付款端點回 `422 ordering.payment-overdue`。否則客人可以重新付款，開放 ATM 後甚至能重新取號、無限延長期限。
- **舊的待付款訂單回填期限、部署後自動取消**：migration 以單一敘述 `UPDATE … RETURNING` 接 `INSERT INTO platform.saga_timer`（沒有 JOIN，形狀同 ADR-016「一個交易寫兩張表」；冪等靠「自動取消時點為空」這個條件，與取消守衛看同一個欄位），
  計時器排在 **migration 執行時間＋1 小時**（migration 在停服前執行，舊 Worker 不認得新的計時器種類，撈到會丟例外、卡住整條計時器佇列；部署若在 migration 之後失敗、舊版繼續跑，要在一小時內重新部署，否則把未觸發的 `ordering.payment-due` 計時器**延後**（把 `fire_at` 往後推）——**不要取消**：取消後重放 0025 不會再排（回填只撈自動取消時點為空的訂單），那批訂單會永遠不被取消；**回滾到舊版之前也一樣先延後**（舊 Worker 不認得這個 saga type，撈到就卡住整條佇列）。**新版上線後要再跑一次 0025 的回填**（冪等），補上「migration 跑完到舊版停掉」之間舊版建的訂單；部署後唯讀確認「待付款且自動取消時點為空」為 0 筆。2026-10-01 覆驗與 BE-64 驗收審查提出）。
- **取消後才入帳**：Ordering 記已收款、維持已取消，發 `RefundRequested`（原路）；退刷或人工由 Payment 依付款方式決定（BE-65）。⚠ **BE-65 之前的已知缺口**：剛入帳的信用卡交易通常還沒關帳，綠界的退刷（`Action=R`）會被拒，Payment 目前只寫稽核、不告警、不發 `PaymentRefunded`——這種訂單會停在「已取消、已收款、未退款」。BE-65 要改成未關帳走放棄（`Action=N`）或退刷被拒時轉待人工退款；在那之前用唯讀查詢「已取消且已收款大於已退款」的訂單人工追蹤（2026-10-01 BE-64 驗收審查提出）。
- **寬限期間仍可自助取消**（訂單還是待付款）；之後若有晚到的付款，走「取消後才入帳」。付款端點**先檢查逾期**（`422 ordering.payment-overdue`），再檢查已取號（`409`）。
- **「同時更新」一律 409**：`payment.concurrent-update`、`ordering.concurrent-update` 與 `payment.instructions-already-issued` 在 `BffHttp.StatusFor` 以**精確比對**對到 409（BE-63），符合 `docs/05` §3「409＝狀態衝突」；`StatusFor` 原本的子字串規則不動。
- **取消原因讓客人看得到，但不做推播**：`OrderCancelled` 加選填 `Source`（`Customer`／`Staff`／`PaymentExpired`，舊事件為 `null`），Order 保存來源；前台訂單頁依來源顯示固定說明，
  **不顯示後台填的取消原因**（內部備註）。系統沒有站內通知、也沒有任何實際送出 Email／LINE／簡訊的程式，完整站內通知另排。
- **人工退款規則**：老闆、會計、營運都能登記；**可分次登記**，累計到應退全額才算完成；**登記後不可修改或刪除**（只追加）；客人帳號由客服另外取得、**不存系統**，備註欄提醒不要填完整帳號。
  契約掛在後台 `PaymentSummary.manualRefund`（每筆付款，不掛訂單——晚到付款是另一筆付款），端點 `POST /v1/orders/{orderId}/payments/{paymentId}/manual-refunds`，BE-65 實作前標 M1b。
  `x-required-role` 首次用陣列表示「任一角色即可」（這個欄位只是文件標註，後端要另加「任一角色」的授權 filter）。

**補記否決**：靠比對取消原因字串判斷「逾期取消」（客人與後台都能自由輸入原因）；在計時器 handler 或取消端點裡取消舊計時器（dispatcher 鎖住正在觸發的那一列，同一列再更新會互等；建單／取消路徑沒有明確交易）；人工退款只允許一次登記全額（使用者選可分次）。
