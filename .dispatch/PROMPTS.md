# 啟動 prompt

**沒有生效中的派工（2026-09-01）。** 兩棵樹都是整合者模式，原始碼一律不准寫。

這一輪跑了兩波，都已通過整合驗收並撤包：

| 包 | 內容 | commit |
|---|---|---|
| BE-34 | 查證「現在卡在哪」#22（查證包，不含修法） | `cb0d2f0` |
| BE-35 | 修 #22 家族——副作用已 commit 就不准 abandon | `213e8a7` |

`.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md` 有完整脈絡。

---

## ★ 下一包的第一優先：`POST /v1/shipments` 的重複風險

BE-34 審 33 個 `BffHttp.ExecuteIdempotentAsync` 呼叫點時找到的，**之前沒有人發現**，
嚴重性高於 #22 本身，**零外部依賴**。

`FulfillmentApplicationService.CreateAsync`（`src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Core/FulfillmentApplicationService.cs:18`）
每次都 `ShipmentId.New()`、不發事件、不改訂單狀態（訂單維持 `ReadyToShip`），
唯一鍵 `ux_shipment_order_tenant_shipment_order` 是 `(tenant_id, shipment_id, order_id)`
——**擋不到「同一批訂單被建成兩張出貨單」**。一層冪等都沒有。

重複那張會被撿貨、被 `dispatch`（寫入 `carrier_cost`），**同一批貨可能出兩次、
物流成本重複入帳**。

BE-35 的新多載已經堵住其中一條觸發路徑（`CompleteAsync` 失敗時不再 abandon），
但 `POST /v1/shipments` **還在用舊多載**，而且真正的根因是底層沒有冪等。
建議修法方向：給 `CreateAsync` 加自然鍵冪等（以 `(tenant_id, 排序後的 orderIds, method)`
或呼叫端傳入的冪等鍵查既有出貨單）。**不需要動 `BffHttp`，也不動契約。**

其餘待辦見 `GreyGray_PM/00-進度總表.md`「下一步」。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。

**BE-35 已經照做（派工前先 commit），子代理整波沒有再撞到這一關。**

---

## 另一條環境教訓（BE-34 踩過）

用 Python 改既有檔案時，`io.open(..., encoding='utf-8-sig')` **讀**的時候有沒有
BOM 都吃，**寫**的時候卻**一定加上 BOM**。這個 repo 的 `.editorconfig` 是
`charset = utf-8`（無 BOM），寫檔一律用 `utf-8`。交付前用
`head -c 3 <檔案> | xxd -p` 確認沒有 `efbbbf`。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
