#!/usr/bin/env bash
# Codex · Stop：收工前的越界檢查。
#
# 這一層對 Codex 比對 Claude 更重要：Codex 大量透過 shell 改檔案
# （Set-Content、Out-File、重導向、git 操作），那些在 PreToolUse 抽不出路徑，
# 擋不了。但 git 看得到每一個被動過的檔案，繞不過去。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="${1:-}"
if [ -z "$payload" ] && [ ! -t 0 ]; then
  payload="$(cat)"
fi

# Codex 已經因為這個 hook 重跑過一輪，不要無限循環
case "$payload" in
  *'"stop_hook_active"'*'true'*) exit 0 ;;
esac

deny() {
  printf '%s\n' "$1" >&2
  printf '{"decision":"deny","reason":"%s"}\n' "$(gg_json_escape "$1")"
  exit 2
}

if gg_has_dispatch; then
  offenders="$(gg_out_of_scope_files | gg_join ' ')"
  [ -n "$offenders" ] || exit 0
  deny "越界了：這些檔案不在派工範圍內 — ${offenders}。生效中的包是 $(gg_active_packages | gg_join '、')，授權路徑在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作。把這些變更還原（git checkout -- <檔案>，新檔就刪掉），或停下來向整合者回報為什麼需要它們。不要自己擴大派工範圍，也不要往下做下一包。"
fi

msgs="$(gg_pm_out_of_sync)"
[ -n "$msgs" ] || exit 0
deny "GreyGray_PM 沒有同步：${msgs} 這是整合者的職責，如果你是實作者，代表 ACTIVE.md 沒有你的 package——停下來回報，不要自己去改 PM 檔案。"
