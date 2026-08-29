# 啟動 prompt（前端第四波）

**直接複製貼上，不用二次加工。** `ACTIVE.md` 裡這三包已經生效。

開 terminal 的方式（前端用 Claude Code）：

```bash
GG_PACKAGE=FE-13 claude
```

**環境變數與 prompt 裡的那一行都要有。** 環境變數給自己開的 terminal 用；
prompt 裡的 `GG_PACKAGE=` 那一行是給 ai-cli fan out 子 agent 用的
（ai-cli 的 `run` 沒有 env 參數，只能走 session_id 綁定）。
兩個都寫，同一份 prompt 兩種開法都能用。

`SessionStart` 會自動把「你這一包能動哪些路徑」送進 context，**所以 prompt 不必重複那些**。

---

## 排程

```
可同時開（三包路徑不相交）   FE-13   FE-14   FE-15
後端 BE-13 通過後才開        FE-12（目前在 ACTIVE.md 裡是註解掉的）
```

三包各自跑 `pnpm --filter <套件>`，不會互相搶——**沒有後端那種 build 競態問題。**

---

## FE-13　後台：出貨、交運與簽收

```
GG_PACKAGE=FE-13

你是 GreyGray Platform 前端第四波的 FE-13。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-13 那一節做。

後台目前走到「買到回報」就斷了，契約裡的四個 fulfillment 端點一個畫面都沒有。
你要做出貨單列表、建立、交運、簽收。

兩件契約明文寫、不要做錯的事：
  一、Order 與 Shipment 是 N:M。契約 description 明寫「這是日常，不是邊緣案例」，
      所以建立出貨單的 UI 必須能一次勾選多張訂單，不是「在訂單頁按出貨」。
  二、carrierCost 是付給物流商的成本，shippingFee 是向客人收的運費。
      畫面上這兩個數字不可以混、不可以相加，label 要讓營運看得懂差別。

自驗照 §6 逐條貼實際指令與實際輸出（含截圖），然後停下來等整合驗收。
```

---

## FE-14　後台：缺貨補償、漲價詢問 ＋ 退款去向回工

```
GG_PACKAGE=FE-14

你是 GreyGray Platform 前端第四波的 FE-14。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-14 那一節做。
再讀 docs/00-decisions.md 的 ADR-023。

這一包有兩件事，第二件是回工：

一、缺貨（unavailable）與漲價（price-changed）兩個端點目前沒有 UI。
    漲價詢問有「到這個時間還沒回覆就自動視為照買」的欄位，畫面要顯示它，
    而且不要做成「等待中」的樣子——藍圖強調系統不會因為等客人而卡住。

二、★ 取消對話框現在是錯的。CancelOrderDialog.tsx 與 CancelOrderLineDialog.tsx
    目前寫著 useState<RefundDestination>('StoredValue')，違反 ADR-023 兩條：
    去向該由客人選（不是營運代選），而且 StoredValue 在 M1b 會被後端明確拒絕
    ——它現在是預設值，營運不改就會送出一個必定失敗的請求。
    改成：不得有預設值、沒選不能送出；StoredValue 顯示但停用並寫「M3 才開放」
    （不要從選單移除）；後端回的業務失敗訊息要原樣顯示，不要吞掉換成通用錯誤。
    共用元件是 RefundDestinationFields.tsx，兩個對話框都在用，先讀它再動手。

驗收條件之一：grep -rn "useState.*StoredValue" apps/admin/ 必須是 0 筆。
自驗照 §6，然後停下來等整合驗收。
```

---

## FE-15　前台：客人看得到自己的品項缺貨與退款

```
GG_PACKAGE=FE-15

你是 GreyGray Platform 前端第四波的 FE-15。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-15 那一節做。

後台已經可以把品項標成缺貨了，但客人在前台看不到這件事——訂單詳情頁不認得
Unavailable 這個狀態，退款金額也沒有呈現。客人只會看到金額對不上而不知道為什麼。

最容易做錯的一件事：讓客人以為整張單被取消了。
契約 description 明寫「該 line 取消並退款，其餘 line 續行，訂單不整張作廢」，
畫面要把「其餘品項照常出貨」講清楚。

先讀既有的訂單詳情頁，這一包是補呈現不是重做頁面。
退款金額用 formatMoney()，不要自己算「原價減退款」——總額後端會回。

不要做「客人選退款去向」的畫面：前台契約沒有那個端點，而且 M1b 只開原路。
自驗照 §6，然後停下來等整合驗收。
```

---

## 通用：三包都適用的三件事

1. **你不可以自己宣告通過。** 只做自驗，逐條貼出**實際指令與實際輸出**，
   不是「已完成」四個字。整合驗收是整合者的事。
2. **每個 mutation 都必須帶 `Idempotency-Key`，而且是 payload-aware 的。**
   MSW handler 不檢查 header，所以 typecheck、build、測試全綠**證明不了**你有帶——
   第二波就是這樣漏掉的。
3. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。同一棵 worktree 有別包在平行工作，
   而且 stash stack 是跨 worktree 共用的。
   FE-10 曾經為了「取得乾淨的驗證基準」跑 `git stash`，把 FE-9 已完成、
   還沒提交的交付整個掃走。閘門會擋，但你本來就不該試。
