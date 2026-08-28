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

## 待決策

### 歷史會員與訂單的遷移
**狀態**：擱置中（2026-08-28）

租用平台看起來已經不給匯出，除非再付 NT$1,399 取得帳號才能匯出。

建議：先確認那 1,399 的方案**實際上有沒有匯出功能**（不要付了才發現只有畫面沒有匯出）。
確認有的話值得付——但目標要收窄成兩件事：

1. **會員清單**（姓名／手機／Email／地址／儲值金餘額）——必須遷。儲值金是你欠客人的負債，
   金額不準等於帳一開始就是錯的。
2. **歷史訂單 CSV 一份**，只做唯讀封存，匯出成獨立查詢表，**不灌進新系統的狀態機**——
   舊訂單的狀態語意跟新系統對不起來。

這題要在 M0 定 schema 前有答案，因為匯出檔的實際欄位會反過來決定 Identity 的欄位設計。
但匯出動作本身不必排進 M0 的關鍵路徑：先把檔案拿到手存起來，實際匯入排到 M1a 上線前。

若確認完全無法匯出：改成「新系統只收新單，舊平台留著查到租約到期」，
客人要重新註冊。schema 最乾淨，代價是客人體驗的一次斷點與儲值金要人工對帳。
