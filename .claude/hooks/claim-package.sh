#!/usr/bin/env bash
# UserPromptSubmit：合併兩件事——
#   1. 從 prompt 裡認出 GG_PACKAGE=<包名> 或 GG_ROLE=leader，綁到這個 session
#      （原本這支腳本唯一的工作，邏輯完全不變）
#   2. 這個 session 第一次送出 prompt 時，順便帶入「你是誰、現在生效的派工是
#      什麼」（原本是 session-brief.sh 在 SessionStart 做的事，2026-08-31 改
#      到這裡做）
#
# ★ 為什麼把 ② 從 SessionStart 搬過來、而且併進同一支腳本、不是另外掛一支
#   新的 UserPromptSubmit hook：
#   使用者層級 ~/.claude/settings.json 另外註冊了 tkflyc-planner 的 SessionStart
#   hook。Claude Code 的 hook entries 跨層級是合併、不是互相覆蓋（官方文件：
#   "Hook entries merge across settings levels rather than replacing each
#   other"），但多個 hook 各自的 additionalContext 撞在同一個事件上要怎麼合併，
#   文件沒有定義。實測結果穩定重現：本地 session-brief.sh 的內容被蓋掉，只有
#   全域那支送達模型。UserPromptSubmit 這個事件全域沒有任何 hook 註冊，改到
#   這裡完全避開跨層級合併的問題——但這個事件底下「同一支腳本內多個 hook
#   entry 之間」的合併語意一樣沒有查證過，保守起見不要疊兩支各自獨立的
#   UserPromptSubmit hook 去冒同一種風險，改成一支腳本自己把兩段內容接在一起
#   輸出一次。純粹是專案層級的改動，不動 ~/.claude/settings.json 或
#   tkflyc-planner 本身一行。
#
# 只在這個 session 的第一次呼叫算「現況」，用 session_id 當去重鍵（跟原本
# session-brief.sh 用的 EXIT trap 保證：不管這次呼叫最後是從哪個分支結束
# （note() 或 bare exit 0），只要現況還沒被送出去，離開前一定會補送一次。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

# marker 放在 .dispatch/.session/（已經因為同一個理由被 .gitignore 排除，
# 不用另外加規則），跟 gg_remember_package／gg_remember_role 用的是同一個
# 目錄，檔名加 .briefed 後綴區分。
payload="$(cat)"
sid="$(gg_session_id "$payload")"

brief_msg=""
brief_flushed=0
if [ -n "$sid" ]; then
  mkdir -p "$GG_ROOT/.dispatch/.session" 2>/dev/null
  marker="$GG_ROOT/.dispatch/.session/$sid.briefed"
  if [ ! -f "$marker" ]; then
    : > "$marker" 2>/dev/null
    gg_resolve_package "$payload"
    gg_resolve_role "$payload"
    if gg_is_leader; then
      brief_msg="你是 Leader（這個專案只開一個 Leader terminal）。你不自己寫原始碼——原始碼一律用 ai-cli fan out 子代理去寫，每個子代理的 prompt 開頭要有 GG_PACKAGE=<包名> 那一行（ai-cli 的 run 沒有 env 參數，包別只能從 prompt 帶進來，UserPromptSubmit 會把它綁到那個子代理的 session_id）。生效中的包：$(gg_active_packages | gg_join '、')。每一包可以直接使用的 prompt 在 .dispatch/PROMPTS.md，用 mcp__ai-cli__run 帶 workFolder 派出去。你自己寫得了的是閘門檔（.dispatch/、.claude/、.codex/）、docs/ 與 GreyGray_PM——那是派工與驗收的工作，不算開工。驗收要自己重跑指令複驗，不要只轉述子代理的自述。"
    elif ! gg_has_dispatch; then
      brief_msg="目前沒有生效中的派工（.dispatch/ACTIVE.md 沒有任何 package）。這代表現在是整合者模式：原始碼一律不准寫，只能動 docs/、management/、閘門自己的檔案與 PM 資料夾。要動原始碼就先在 ACTIVE.md 開一筆 package，留下軌跡。"
    elif gg_role_unknown; then
      brief_msg="這個 session 沒有宣告身分，而現在有派工生效（$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，也擋下閘門檔本身——這是刻意的 fail-closed，因為「忘記宣告的實作者」與「Leader」從外面看一模一樣。要做事請擇一宣告：實作者在 prompt 開頭寫 GG_PACKAGE=<包名>（或用 GG_PACKAGE=<包名> 開 terminal），Leader 寫 GG_ROLE=leader。不要自己改 ACTIVE.md 繞過去。"
    elif ! gg_package_valid; then
      brief_msg="GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，直到包名對上為止。停下來確認你要做的是哪一包，不要自己改 ACTIVE.md。"
    else
      brief_msg="你受管的包是 $(gg_scope_label)。派工書：$(gg_active_docs | gg_join '、')。你這一包獨佔的路徑：$(gg_allow_list | gg_join '　')。另外 docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了（交付說明、runbook 這些文件不算「開工」）。這個範圍以外的原始碼一律不准寫，PreToolUse 閘門會擋、收工前 git diff 也會再查一次。需要範圍外的檔案就停下來回報，不要自己改。自驗全過之後就停，不要往下做下一包——整合驗收不是你的工作。"
    fi
  fi
fi

flush_brief() {
  if [ -n "$brief_msg" ] && [ "$brief_flushed" -eq 0 ]; then
    brief_flushed=1
    printf '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"%s"}}\n' "$(gg_json_escape "$brief_msg")"
  fi
}
trap flush_brief EXIT

note() {
  local text="$1"
  if [ -n "$brief_msg" ]; then
    text="$brief_msg

---

$text"
    brief_flushed=1
  fi
  printf '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"%s"}}\n' "$(gg_json_escape "$text")"
  exit 0
}

pkg="$(printf '%s' "$payload" | grep -oE 'GG_PACKAGE=[A-Za-z0-9._-]+' | head -1 | sed 's/^GG_PACKAGE=//')"
role="$(printf '%s' "$payload" | grep -oE 'GG_ROLE=[A-Za-z0-9._-]+' | head -1 | sed 's/^GG_ROLE=//')"

# ── 包別優先於角色，這個順序是刻意的 ──────────────────────────
# 子代理的 prompt 一定帶 GG_PACKAGE=。萬一同一段文字裡也出現 GG_ROLE=leader
# （例如 Leader 把自己那段 prompt 連同說明整份貼給子代理），
# 也絕不能讓子代理升級成 Leader——Leader 寫得了閘門檔。
if [ -n "$pkg" ]; then
  claimed="$GG_ROOT/.dispatch/.session/$sid"
  rolef="$claimed.role"
  if [ -f "$rolef" ]; then
    note "這個 session 已經是 Leader，不接受改綁成 ${pkg}。Leader 不自己動手，請用 ai-cli fan out 一個子代理去做那一包。"
  fi
  if [ -f "$claimed" ]; then
    cur="$(cat "$claimed")"
    [ "$cur" = "$pkg" ] || note "這個 session 已經綁定 ${cur}，不接受改成 ${pkg}。要換包請重開一個 session。閘門仍照 ${cur} 判斷。"
  else
    if ! gg_active_packages | grep -qxF "$pkg"; then
      note "prompt 宣告了 GG_PACKAGE=${pkg}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。沒有綁定，閘門會擋下所有原始碼寫入。請向整合者確認包名。"
    fi
    gg_remember_package "$sid" "$pkg" || exit 0
    GG_PACKAGE="$pkg" note "已綁定：這個 session 受 ${pkg} 管。授權寫入的路徑只有這些：$(GG_PACKAGE="$pkg" gg_allow_list | gg_join '　')。另外 docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。範圍外的原始碼一律不准寫。自驗全過就停，不要往下做別包，也不能自己宣告通過。"
  fi
fi

# ── 沒有包別時才看角色 ────────────────────────────────────────
if [ "$role" = "leader" ]; then
  claimed="$GG_ROOT/.dispatch/.session/$sid"
  rolef="$claimed.role"
  if [ ! -f "$rolef" ]; then
    if [ -f "$claimed" ]; then
      note "這個 session 已經綁定 $(cat "$claimed")，不能再宣告成 Leader。要當 Leader 請重開一個 session。"
    fi
    gg_remember_role "$sid" leader || exit 0
    GG_ROLE=leader note "已宣告 Leader。你的工作是派工與驗收，不是自己寫原始碼——原始碼一律用 ai-cli fan out 子代理去寫，每個子代理的 prompt 開頭要有 GG_PACKAGE=<包名> 那一行。你寫得了的是閘門檔（.dispatch/、.claude/、.codex/）、docs/ 與 GreyGray_PM。生效中的包：$(gg_active_packages | gg_join '、')。每包可直接使用的 prompt 在 .dispatch/PROMPTS.md。"
  fi
fi
exit 0
