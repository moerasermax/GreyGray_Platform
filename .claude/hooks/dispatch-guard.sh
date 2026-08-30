#!/usr/bin/env bash
# Claude Code · PreToolUse：兩件事
#   1. 沒有派工書授權的路徑，不准寫（Write／Edit）
#   2. 會毀掉別人未提交交付的 shell 指令，不准跑（Bash／PowerShell）
# 判斷邏輯在 .dispatch/，與 Codex 那邊共用同一份。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"
. "$GG_ROOT/.dispatch/shell-guard-lib.sh"

payload="$(cat)"
gg_resolve_package "$payload"
gg_resolve_role "$payload"

deny() {
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}\n' "$(gg_json_escape "$1")"
  exit 0
}

# ── 危險 shell 指令 ──────────────────────────────────────────────
cmdtext="$(printf '%s' "$payload" | sed -n 's/.*"command"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
# ★ 修正：原本這一段有 `&& gg_has_dispatch`，等於「撤包之後就不擋」。
# 但危險指令要防的是「工作區裡別人未提交的交付」，那跟有沒有生效派工無關——
# 驗收完還沒 commit 的交付正是最脆弱的時候（FE-10 事故就是這個情境）。
# 而且 stash stack 是跨 worktree 共用的，這棵樹沒派工不代表另一棵沒有。
if [ -n "$cmdtext" ]; then
  if why="$(gg_dangerous_shell_reason "$cmdtext")"; then
    # git commit 只放行 Leader（明確宣告 GG_ROLE=leader 的那個 session）。
    # 原本寫「GG_PACKAGE 沒設就放行」，但那把【身分不明】也一起放行了——
    # 而身分不明在這套閘門的其他每一處都是 fail-closed。
    # 其餘（stash／reset --hard／clean／全樹還原）對所有人都擋，
    # 因為它們會毀掉工作區裡別人未提交的交付。
    case "$cmdtext" in
      *"git commit"*) gg_is_leader && why="" ;;
    esac
    [ -n "$why" ] && deny "$why"
  fi
fi

# ── 派工範圍 ────────────────────────────────────────────────────
case "$payload" in
  *'"Write"'*|*'"Edit"'*|*'"MultiEdit"'*|*'"NotebookEdit"'*) ;;
  *) exit 0 ;;
esac

if ! gg_package_valid; then
  deny "GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。包名拼錯不該變成什麼都能寫，所以這裡一律擋下。改成正確的包名，或請整合者更新 ACTIVE.md。"
fi

bad=""
while IFS= read -r p; do
  [ -n "$p" ] || continue
  rel="$(gg_relpath "$p")"
  gg_path_allowed "$rel" || bad="$bad $rel"
done <<EOF
$(gg_extract_paths "$payload")
EOF

[ -n "$bad" ] || exit 0

if gg_has_dispatch; then
  deny "這個路徑不在你這一包的派工範圍內：${bad}。你受管的包是 $(gg_scope_label)，授權路徑寫在 .dispatch/ACTIVE.md。越界等於覆蓋掉別人的工作——停下來回報，不要自己改，也不要自己擴大範圍。"
else
  deny "沒有生效中的派工書，不得寫入原始碼：${bad}。規則是只能執行整合者發出的派工書所授權的範圍（.dispatch/ACTIVE.md 現在沒有任何 package）。整合者自己要動原始碼時，也要在 ACTIVE.md 開一筆 package 留下軌跡。"
fi
