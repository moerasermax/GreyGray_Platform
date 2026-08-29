#!/usr/bin/env bash
# Codex · PreToolUse：兩件事
#   1. 沒有派工書授權的路徑，不准寫（apply_patch／Edit／Write／MultiEdit）
#   2. 會毀掉別人未提交交付的 shell 指令，不准跑
#
# 與 Claude 版的三個差異（都是實測 codex 0.150.1 之後才知道的）：
#   1. payload 可能從 argv[0] 進來，也可能從 stdin。兩種都要接。
#   2. Codex 寫檔主要走 apply_patch，路徑不在 tool_input.file_path，
#      而是藏在 patch 內文的 *** Add/Update/Delete File: 標記裡。
#      只看 file_path 的話，Codex 這邊等於沒有閘門。
#   3. deny 的形式是 {"decision":"deny","reason":...} 加 exit 2，
#      不是 Claude 的 hookSpecificOutput.permissionDecision。
#
# 判斷邏輯本身與 Claude 版共用 .dispatch/，讀的是同一份 ACTIVE.md。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"
. "$GG_ROOT/.dispatch/shell-guard-lib.sh"

payload="${1:-}"
if [ -z "$payload" ]; then
  payload="$(cat)"
fi
[ -n "$payload" ] || exit 0
gg_resolve_package "$payload"

deny() {
  printf '%s\n' "$1" >&2
  printf '{"decision":"deny","reason":"%s"}\n' "$(gg_json_escape "$1")"
  exit 2
}

# ── 危險 shell 指令 ──────────────────────────────────────────────
cmdtext="$(printf '%s' "$payload" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
if [ -n "$cmdtext" ] && gg_has_dispatch; then
  if why="$(gg_dangerous_shell_reason "$cmdtext")"; then
    # git commit 只擋實作者——整合者要靠它提交。
    # 其餘（stash／reset --hard／clean／全樹還原）對所有人都擋。
    case "$cmdtext" in
      *"git commit"*) [ -n "${GG_PACKAGE:-}" ] || why="" ;;
    esac
    [ -n "$why" ] && deny "$why"
  fi
fi

# ── 派工範圍 ────────────────────────────────────────────────────
case "$payload" in
  *apply_patch*|*'"Write"'*|*'"Edit"'*|*'"MultiEdit"'*) ;;
  *) exit 0 ;;
esac

if ! gg_package_valid; then
  deny "GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。包名拼錯不該變成什麼都能寫，所以這裡一律擋下。"
fi

bad=""
while IFS= read -r p; do
  [ -n "$p" ] || continue
  rel="$(gg_relpath "$p")"
  gg_path_allowed "$rel" || bad="$bad $rel"
done <<EOF
$(gg_extract_paths "$payload")
EOF

# 抽不出路徑（例如純 shell 指令）就不在這裡擋——擋不準會誤傷。
# 那一層交給 stop-gate.sh 收工前的 git diff，那個抓得到任何寫入方式。
[ -n "$bad" ] || exit 0

if gg_has_dispatch; then
  deny "這個路徑不在你這一包的派工範圍內：${bad}。你受管的包是 $(gg_scope_label)，授權路徑寫在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作——停下來回報，不要自己改，也不要自己擴大範圍。"
else
  deny "沒有生效中的派工書，不得寫入原始碼：${bad}。規則是只能執行整合者發出的派工書所授權的範圍（.dispatch/ACTIVE.md 現在沒有任何 package）。要開工請先向整合者要派工。"
fi
