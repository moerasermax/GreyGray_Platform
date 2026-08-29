#!/usr/bin/env bash
# Claude Code · Stop：收工前的兩道檢查，依模式擇一。
#
#   實作者模式（ACTIVE.md 有 package）→ git diff 越界檢查。
#     這一層是必要的：PreToolUse 只看得到 Write／Edit，
#     用 Bash（sed -i、heredoc、重導向）寫的檔案繞得過去，但繞不過 git。
#   整合者模式（沒有 package）→ GreyGray_PM 同步檢查。
#     更新進度表是整合者的職責，不是實作者的，所以只在這個模式下查。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="$(cat)"
case "$payload" in
  *'"stop_hook_active"'*'true'*) exit 0 ;;
esac

emit_block() {
  printf '{"decision":"block","reason":"%s","systemMessage":"%s"}\n' \
    "$(gg_json_escape "$1")" "$(gg_json_escape "$2")"
  exit 0
}

if gg_has_dispatch; then
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || exit 0
  emit_block \
    "越界了：這些檔案不在派工範圍內 — ${offenders}。生效中的包是 $(gg_active_packages | gg_join '、')，授權路徑在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作。把這些變更還原（git checkout -- <檔案>，新檔就刪掉），或停下來向整合者回報為什麼需要它們。不要自己擴大派工範圍。" \
    "越界：有檔案不在派工範圍內，已擋下收工"
fi

msgs="$(gg_pm_out_of_sync)"
[ -n "$msgs" ] || exit 0
emit_block \
  "GreyGray_PM 沒有同步：${msgs} 收工前請更新，規則見 GreyGray_PM/README.md。事實要能重跑驗證（commit SHA、測試數字、端點數），標記通過之前要寫得出「怎麼驗的」。真的是誤判就 touch 較舊的那個檔案。" \
  "GreyGray_PM 進度未同步，已擋下收工"
