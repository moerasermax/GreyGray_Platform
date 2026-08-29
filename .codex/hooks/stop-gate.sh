#!/usr/bin/env bash
# Codex · Stop：收工前的檢查，依「這個 session 是誰」分兩條路。
#
# 這一層對 Codex 比對 Claude 更重要：Codex 大量透過 shell 改檔案
# （Set-Content、Out-File、重導向、git 操作），那些在 PreToolUse 抽不出路徑，
# 擋不了。但 git 看得到每一個被動過的檔案，繞不過去。
#
#   實作者（有綁定包別）→ 只查越界。
#   整合者（沒有綁定包別）→ 越界 ＋ 每包都要有啟動 prompt ＋ GreyGray_PM 同步。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="${1:-}"
if [ -z "$payload" ] && [ ! -t 0 ]; then
  payload="$(cat)"
fi
gg_resolve_package "$payload"
gg_resolve_role "$payload"

# Codex 已經因為這個 hook 重跑過一輪，不要無限循環
case "$payload" in
  *'"stop_hook_active"'*'true'*) exit 0 ;;
esac

deny() {
  printf '%s\n' "$1" >&2
  printf '{"decision":"deny","reason":"%s"}\n' "$(gg_json_escape "$1")"
  exit 2
}

check_out_of_scope() {
  local offenders
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || return 0
  deny "越界了：這些檔案不在派工範圍內 — ${offenders}。生效中的包是 $(gg_active_packages | gg_join '、')，授權路徑在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作。把這些變更還原（git checkout -- <檔案>，新檔就刪掉），或停下來向整合者回報為什麼需要它們。不要自己擴大派工範圍，也不要往下做下一包。"
}

# ── 實作者：查完越界就結束 ────────────────────────────────────────
if [ -n "${GG_PACKAGE:-}" ]; then
  check_out_of_scope
  exit 0
fi

# ── 身分不明：有派工生效卻沒宣告是誰 ──────────────────────────────
if gg_role_unknown; then
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || exit 0
  deny "這個 session 沒有宣告身分，卻動了這些檔案 — ${offenders}。有派工生效時（現在是 $(gg_active_packages | gg_join '、')），閘門分不出「忘記宣告的實作者」與「Leader」，所以一律 fail-closed。要做事請擇一宣告：實作者在 prompt 開頭寫 GG_PACKAGE=<包名>，Leader 寫 GG_ROLE=leader。"
fi

# ── 以下是 Leader（或什麼都還沒派的空窗期）────────────────────────
check_out_of_scope

# 派工書寫完就要給得出啟動 prompt（使用者明講的要求）。
missing="$(gg_prompts_missing)"
if [ -n "$missing" ]; then
  deny "這幾包在 .dispatch/ACTIVE.md 生效了，但 .dispatch/PROMPTS.md 裡沒有它們的啟動 prompt：${missing}。派工書寫完就要給得出可以直接複製貼上的 prompt，不能讓使用者自己回去讀派工書拼一段出來。每包一段，內容要有：GG_PACKAGE=<包名> 那一行（ai-cli fan out 靠它綁 session_id）、要讀哪份派工書的哪一節、一句話講清楚這包在做什麼、以及「自驗完就停，不可自己宣告通過」。SessionStart 會自動注入授權路徑，所以 prompt 不必重複那些。"
fi

msgs="$(gg_pm_out_of_sync)"
[ -n "$msgs" ] || exit 0
deny "GreyGray_PM 沒有同步：${msgs} 這是整合者的職責，如果你是實作者，代表 ACTIVE.md 沒有你的 package——停下來回報，不要自己去改 PM 檔案。"
