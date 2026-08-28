# 代購平台

出國採購開團 ＋ 本地批發現貨並存的代購業務系統。前後端徹底分離，後端不輸出任何畫面。

模組化單體，十四個限界上下文，邊界由**編譯期與資料庫權限雙重強制**。
帳務以複式記帳為底，兩種模式共用同一組科目，差別只在存貨從哪來、成本何時確定。

| 項目 | 值 |
|---|---|
| 技術棧 | .NET 10 LTS ＋ ASP.NET Core ＋ PostgreSQL 17 ＋ Valkey |
| 部署載體 | Native ＋ NSSM（**不用 Docker**，理由見 `docs/00-decisions.md` ADR-003） |
| 正式機 | YC（MSI GL73 8SDK 筆電）· 五千號段 port |
| 前台樣式 | 韓系柔美 Soft Seoul（ADR-009） |
| 完整藍圖 | [代購平台後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖） |
| 前台樣板 | [代購前台 五套風格樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b) |

目前狀態看 [STATE.md](STATE.md)。要接手開發看 [docs/03-M0工作包.md](docs/03-M0工作包.md)。

## 目錄

```
Daigou.slnx                     49 個專案
Directory.Build.props           TargetFramework、Nullable、TreatWarningsAsErrors
Directory.Packages.props        中央套件版本管理（版本一律釘死）
dotnet.config                   dotnet test 的 MTP opt-in（此 SDK 版本尚未生效，見 ops/test.ps1）

src/
  Shared.Kernel/                Money · Currency · Result · IClock —— 無業務語意的型別
  Platform.Abstractions/        事件 · Outbox 發布 · Idempotency · Saga Timer 的「介面」
  Platform/                     上述的實作（EF Core 在這一層才出現）
  Modules/<模組>/
    *.Contracts/                public：ID、DTO、介面、事件定義（唯一可被別人參考的組件）
    *.Core/                     internal：聚合根、值物件、狀態機、規則
    *.Infra/                    internal：DbContext（只 map 自己的 schema）、repository
  Hosts/
    Daigou.Api.Storefront/      公開 BFF  :5000
    Daigou.Api.Admin/           內部 BFF  :5001（Cloudflare Access 之後）
    Daigou.Worker/              Outbox · Saga · 排程（無 listener）

tests/Daigou.Architecture.Tests/  組件參考規則的斷言，違規 build fail

db/migrations/                  SQL migration（M0 只有 schema、role、platform 三張表）
ops/                            建置、測試、部署腳本
docs/                           決策紀錄與規格
```

## 建置與測試

```powershell
dotnet build .\Daigou.slnx
.\ops\test.ps1                 # 不要用 dotnet test，理由寫在腳本裡
```

## 四條硬規則

1. 模組只能參考其他模組的 `*.Contracts`，**不得參考** `*.Core`。Core 裡的型別一律 `internal`。
2. 跨模組取資料走 Contracts 介面（同步）或訂閱事件建自己的 read model（非同步）。
   **禁止跨 schema JOIN，沒有例外。**
3. 每個模組一個 Postgres role，只 `GRANT` 自己的 schema。
4. 事件只承載「已發生的事實 ＋ 識別碼」，不承載對方模組的內部模型。需要細節就回頭呼叫 Contracts。

第 1、3 條由 `tests/Daigou.Architecture.Tests` 與 `db/migrations/0001` 分別強制。
第 2、4 條靠 review——所以它們寫在這裡。
