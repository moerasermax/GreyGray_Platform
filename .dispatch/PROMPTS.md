# 啟動 prompt

**沒有生效中的派工（2026-09-01）。** 兩棵樹都是整合者模式，原始碼一律不准寫。

BE-34（查證「現在卡在哪」#22 checkout 幽靈訂單／冪等 abandon，後端 `cb0d2f0`）
已通過整合驗收並撤包。它是**查證包、不含修法**，結論推翻了既有文件的說法
（「同一把 Idempotency-Key 重試會建出第二張訂單」是錯的），並找到一個之前沒人
發現的 ★ 重複風險 `POST /v1/shipments`。詳見 `.dispatch/ACTIVE.md`
「已經通過、不再生效的」清單、`.dispatch/reports/BE-34.md` 與
`GreyGray_PM/00-進度總表.md`。

---

## ★ 下一波派工前，先做這一件（BE-34 查出來的閘門盲點）

**Leader 派工前應該先把閘門檔（`.dispatch/ACTIVE.md`、`.dispatch/PROMPTS.md`、
`.dispatch/.selftest-stamp`）commit 掉，再派子代理。**

原因：`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的那三個檔在子代理眼中就是
「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原 `ACTIVE.md`
等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓 `audit-dispatch.sh`
第 ⑩ 項由綠轉紅。BE-34 的子代理兩次都正確拒絕了，但**只要派工前沒先 commit，
每一包收工都會撞到這一關**。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
