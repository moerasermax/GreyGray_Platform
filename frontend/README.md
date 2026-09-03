# GreyGray 前端

兩個 Next.js 15 app ＋ 兩個共用套件，pnpm workspace。
**與後端完全分離**——唯一的接觸面是 `docs/05-API契約.md` 與 `docs/api/openapi.*.yaml`。

```
frontend/
  apps/storefront/     客人端 · Soft Seoul（ADR-009）· :5002 → BFF :5000
  apps/admin/          團隊端 · 中性密集儀表板       · :5003 → BFF :5001
  packages/ui/         設計 token。顏色與尺寸的唯一來源
  packages/api-client/ HTTP 傳輸、Money、Problem Details、冪等鍵
```

## 開發

```bash
cd frontend
pnpm install
cp apps/storefront/.env.example apps/storefront/.env.local
cp apps/admin/.env.example apps/admin/.env.local

pnpm dev                 # 兩個一起跑
pnpm dev:storefront      # 只跑前台 :5002
pnpm dev:admin           # 只跑後台 :5003

pnpm typecheck
pnpm api:generate        # 從 OpenAPI 重新產型別
```

後端還沒有任何端點時，前端跑在 mock 上（見 `docs/06-前端工作包.md` 的 FE-1）。

## 四條規則

1. **顏色與尺寸只從 token 來。** 元件裡不准出現 raw hex，不准出現 `rounded-[18px]` 這種任意值。
   四個人各自挑一個粉紅色，出來就是四個產品。
2. **不做金額運算。** 只呼叫 `formatMoney()`。加總、分攤、含運總額由後端回傳。
   前端算了，帳就有兩個來源，而其中一個永遠沒有測試。
   唯一例外：商品頁小計預覽（`subtotalPreview`，ADR-033）——只用於顯示，購物車以後端為準。
3. **不猜業務規則。** 「這個團還能不能下單」讀 `isAcceptingOrders`，
   不要自己拿 `closesAt` 跟現在時間比——客戶端時鐘不可信，截團是後端的 Saga Timer 說了算。
4. **不直接 `fetch`。** 一律走 `@greygray/api-client`——
   session cookie、冪等鍵、problem+json 的處理都在那一層，繞過去就會漏掉其中一項。

## 型別是產生出來的，不要手改

`packages/api-client/src/types.storefront.ts` 與 `types.admin.ts` 由
`pnpm api:generate` 從 `docs/api/openapi.*.yaml` 產出。

手改的下場是：下次重新產生就沒了，而且中間那段時間會跟後端悄悄對不起來。
**要改契約請改 YAML**，並回 `docs/05-API契約.md` 說明理由——那份是凍結的。

## 正式機部署

`output: 'standalone'`，`dotnet publish` 之外的第四、第五個 native process，
以 NSSM 註冊（ADR-003，不用 Docker），沿用既有 12 個服務的
S4U ＋ BootTrigger ＋ 每 5 分鐘 watchdog 慣例。

port 用 5002 / 5003，避開既有占用（3000 · 3300 · 3400 · 3456 · 3500 ·
3600/3601 · 3700 · 3800 · 8000 · 11434 · 19530/19531 · 27017）。
新服務要登記進 prod-monitor 的 process／port 指紋。
