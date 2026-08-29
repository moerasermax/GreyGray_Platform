#!/usr/bin/env bash
# Claude Code · Stop：收工前的檢查，依「這個 session 是誰」分兩條路。
#
#   實作者（有綁定包別）→ 只查 git diff 越界。
#     這一層是必要的：PreToolUse 只看得到 Write／Edit，
#     用 Bash（sed -i、heredoc、重導向）寫的檔案繞得過去，但繞不過 git。
#
#   整合者（沒有綁定包別）→ 三件事：
#     1. 越界檢查（沒開 package 就不准寫原始碼，這條對整合者一樣成立）
#     2. 每個生效中的包都要有可以直接貼的啟動 prompt
#     3. GreyGray_PM 同步（更新進度表是整合者的職責，不是實作者的）
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="$(cat)"
gg_resolve_package "$payload"
gg_resolve_role "$payload"
case "$payload" in
  *'"stop_hook_active"'*'true'*) exit 0 ;;
esac

emit_block() {
  printf '{"decision":"block","reason":"%s","systemMessage":"%s"}\n' \
    "$(gg_json_escape "$1")" "$(gg_json_escape "$2")"
  exit 0
}

check_out_of_scope() {
  local offenders
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || return 0
  emit_block \
    "越界了：這些檔案不在派工範圍內 — ${offenders}。生效中的包是 $(gg_active_packages | gg_join '、')，授權路徑在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作。把這些變更還原（git checkout -- <檔案>，新檔就刪掉），或停下來向整合者回報為什麼需要它們。不要自己擴大派工範圍。" \
    "越界：有檔案不在派工範圍內，已擋下收工"
}

# ── 實作者：查完越界就結束 ────────────────────────────────────────
if [ -n "${GG_PACKAGE:-}" ]; then
  check_out_of_scope
  exit 0
fi

# ── 身分不明：有派工生效卻沒宣告是誰 ──────────────────────────────
# 沒改到東西就放行（純粹沒宣告不該變成收工的阻礙），
# 但只要動了範圍外的檔案就擋，並且告訴他該怎麼宣告。
if gg_role_unknown; then
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || exit 0
  emit_block     "這個 session 沒有宣告身分，卻動了這些檔案 — ${offenders}。有派工生效時（現在是 $(gg_active_packages | gg_join '、')），閘門分不出「忘記宣告的實作者」與「Leader」，所以一律 fail-closed。要做事請擇一宣告：實作者在 prompt 開頭寫 GG_PACKAGE=<包名>，Leader 寫 GG_ROLE=leader。把這些變更還原，或宣告身分後重做。"     "身分未宣告且有範圍外的變更，已擋下收工"
fi

# ── 以下是 Leader（或什麼都還沒派的空窗期）────────────────────────
check_out_of_scope

# 派工書寫完就要給得出啟動 prompt。
# 這條是使用者明講的要求：「派工書建立完要給我他們的啟動 prompt」。
# 不擋的話症狀是——派工書寫得很完整、ACTIVE.md 也啟用了，
# 但使用者還要自己回去讀派工書、自己拼一段 prompt 出來才開得了工。
# ★ 派工書邏輯稽核。使用者的要求：「檢查到完全沒有邏輯漏洞」。
#
# 手審抓得到一次，抓不到每一次——第六波派工書寫錯三個前提，
# 第七波第一版又把事件 handler 的註冊檔劃給了錯的包，BE-18 會直接做不完。
# 那幾個錯的共同點是都可以機械查出來。所以讓機器查，不要靠我記得。
if [ -f "$GG_ROOT/.dispatch/audit-dispatch.sh" ]; then
  audit_out="$(bash "$GG_ROOT/.dispatch/audit-dispatch.sh" 2>&1)"
  audit_rc=$?
  if [ "$audit_rc" -ne 0 ]; then
    fails="$(printf '%s' "$audit_out" | grep -F '✗' | sed 's/^ *//' | gg_join '；')"
    emit_block \
      "派工書邏輯稽核未通過：${fails} —— 每一條都會讓子代理做不完或做錯（allow 路徑不存在、migration 撞號、兩包搶同一個檔、引用了不存在的檔或行號、有包沒有啟動 prompt）。跑 bash .dispatch/audit-dispatch.sh 看完整輸出，修好再收工。不要因為「應該沒差」就放過——第六波與第七波各發生過一次，都是這一類。" \
      "派工書邏輯稽核未通過，已擋下收工"
  fi
fi

missing="$(gg_prompts_missing)"
if [ -n "$missing" ]; then
  emit_block \
    "這幾包在 .dispatch/ACTIVE.md 生效了，但 .dispatch/PROMPTS.md 裡沒有它們的啟動 prompt：${missing}。派工書寫完就要給得出可以直接複製貼上的 prompt，不能讓使用者自己回去讀派工書拼一段出來。每包一段，內容要有：GG_PACKAGE=<包名> 那一行（ai-cli fan out 靠它綁 session_id）、要讀哪份派工書的哪一節、一句話講清楚這包在做什麼、以及「自驗完就停，不可自己宣告通過」。SessionStart 會自動注入授權路徑，所以 prompt 不必重複那些。" \
    "有派工沒有啟動 prompt，已擋下收工"
fi

msgs="$(gg_pm_out_of_sync)"
[ -n "$msgs" ] || exit 0
emit_block \
  "GreyGray_PM 沒有同步：${msgs} 收工前請更新，規則見 GreyGray_PM/README.md。事實要能重跑驗證（commit SHA、測試數字、端點數），標記通過之前要寫得出「怎麼驗的」。真的是誤判就 touch 較舊的那個檔案。" \
  "GreyGray_PM 進度未同步，已擋下收工"
