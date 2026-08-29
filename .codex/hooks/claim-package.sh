#!/usr/bin/env bash
# Codex · UserPromptSubmit：從 prompt 裡認出 GG_PACKAGE=<包名> 或 GG_ROLE=leader，綁到這個 session。
#
# 這是給「lead 用 ai-cli fan out 子 agent」那條路用的：
# ai-cli 的 run 沒有環境變數參數，所以包別只能從 prompt 帶進來。
# 綁定的檔名是 session_id，多個子 agent 同時跑不會互相蓋掉。
#
# 只認第一次宣告。已經綁過的 session 再送 GG_PACKAGE= 會被拒絕——
# 否則子 agent 只要在後續訊息裡寫一句就能自己換包，那閘門就沒有意義了。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="${1:-}"
if [ -z "$payload" ]; then payload="$(cat)"; fi
note() {
  printf '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"%s"}}\n' "$(gg_json_escape "$1")"
  exit 0
}

sid="$(gg_session_id "$payload")"
[ -n "$sid" ] || exit 0

claimed="$GG_ROOT/.dispatch/.session/$sid"
rolef="$claimed.role"
pkg="$(printf '%s' "$payload" | grep -oE 'GG_PACKAGE=[A-Za-z0-9._-]+' | head -1 | sed 's/^GG_PACKAGE=//')"
role="$(printf '%s' "$payload" | grep -oE 'GG_ROLE=[A-Za-z0-9._-]+' | head -1 | sed 's/^GG_ROLE=//')"

# ── 包別優先於角色，這個順序是刻意的 ──────────────────────────
# 子代理的 prompt 一定帶 GG_PACKAGE=。萬一同一段文字裡也出現 GG_ROLE=leader
# （例如 Leader 把自己那段 prompt 連同說明整份貼給子代理），
# 也絕不能讓子代理升級成 Leader——Leader 寫得了閘門檔。
if [ -n "$pkg" ]; then
  if [ -f "$rolef" ]; then
    note "這個 session 已經是 Leader，不接受改綁成 ${pkg}。Leader 不自己動手，請用 ai-cli fan out 一個子代理去做那一包。"
  fi
  if [ -f "$claimed" ]; then
    cur="$(cat "$claimed")"
    [ "$cur" = "$pkg" ] && exit 0
    note "這個 session 已經綁定 ${cur}，不接受改成 ${pkg}。要換包請重開一個 session。閘門仍照 ${cur} 判斷。"
  fi
  if ! gg_active_packages | grep -qxF "$pkg"; then
    note "prompt 宣告了 GG_PACKAGE=${pkg}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。沒有綁定，閘門會擋下所有原始碼寫入。請向整合者確認包名。"
  fi
  gg_remember_package "$sid" "$pkg" || exit 0
  GG_PACKAGE="$pkg" note "已綁定：這個 session 受 ${pkg} 管。授權寫入的路徑只有這些：$(GG_PACKAGE="$pkg" gg_allow_list | gg_join '　')。另外 docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。範圍外的原始碼一律不准寫。自驗全過就停，不要往下做別包，也不能自己宣告通過。"
fi

# ── 沒有包別時才看角色 ────────────────────────────────────────
if [ "$role" = "leader" ]; then
  [ -f "$rolef" ] && exit 0
  if [ -f "$claimed" ]; then
    note "這個 session 已經綁定 $(cat "$claimed")，不能再宣告成 Leader。要當 Leader 請重開一個 session。"
  fi
  gg_remember_role "$sid" leader || exit 0
  GG_ROLE=leader note "已宣告 Leader。你的工作是派工與驗收，不是自己寫原始碼——原始碼一律用 ai-cli fan out 子代理去寫，每個子代理的 prompt 開頭要有 GG_PACKAGE=<包名> 那一行。你寫得了的是閘門檔（.dispatch/、.claude/、.codex/）、docs/ 與 GreyGray_PM。生效中的包：$(gg_active_packages | gg_join '、')。每包可直接使用的 prompt 在 .dispatch/PROMPTS.md。"
fi
exit 0
