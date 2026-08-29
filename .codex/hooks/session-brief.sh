#!/usr/bin/env bash
# Codex · SessionStart：把「現在生效的派工是什麼」送進 context。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="${1:-}"
if [ -z "$payload" ] && [ ! -t 0 ]; then payload="$(cat)"; fi
gg_resolve_package "$payload"
gg_resolve_role "$payload"

if gg_is_leader; then
  msg="你是 Leader（這個專案只開一個 Leader terminal）。你不自己寫原始碼——原始碼一律用 ai-cli fan out 子代理去寫，每個子代理的 prompt 開頭要有 GG_PACKAGE=<包名> 那一行（ai-cli 的 run 沒有 env 參數，包別只能從 prompt 帶進來，UserPromptSubmit 會把它綁到那個子代理的 session_id）。生效中的包：$(gg_active_packages | gg_join '、')。每一包可以直接使用的 prompt 在 .dispatch/PROMPTS.md，用 mcp__ai-cli__run 帶 workFolder 派出去。你自己寫得了的是閘門檔（.dispatch/、.claude/、.codex/）、docs/ 與 GreyGray_PM——那是派工與驗收的工作，不算開工。驗收要自己重跑指令複驗，不要只轉述子代理的自述。"
elif ! gg_has_dispatch; then
  msg="目前沒有生效中的派工（.dispatch/ACTIVE.md 沒有任何 package）。這代表現在是整合者模式：原始碼一律不准寫，只能動 docs/、management/、閘門自己的檔案與 PM 資料夾。要動原始碼就先在 ACTIVE.md 開一筆 package，留下軌跡。"
elif gg_role_unknown; then
  msg="這個 session 沒有宣告身分，而現在有派工生效（$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，也擋下閘門檔本身——這是刻意的 fail-closed，因為「忘記宣告的實作者」與「Leader」從外面看一模一樣。要做事請擇一宣告：實作者在 prompt 開頭寫 GG_PACKAGE=<包名>（或用 GG_PACKAGE=<包名> 開 terminal），Leader 寫 GG_ROLE=leader。不要自己改 ACTIVE.md 繞過去。"
elif ! gg_package_valid; then
  msg="GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，直到包名對上為止。停下來確認你要做的是哪一包，不要自己改 ACTIVE.md。"
else
  msg="你受管的包是 $(gg_scope_label)。派工書：$(gg_active_docs | gg_join '、')。你這一包獨佔的路徑：$(gg_allow_list | gg_join '　')。另外 docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了（交付說明、runbook 這些文件不算「開工」）。這個範圍以外的原始碼一律不准寫，PreToolUse 閘門會擋、收工前 git diff 也會再查一次。需要範圍外的檔案就停下來回報，不要自己改。自驗全過之後就停，不要往下做下一包——整合驗收不是你的工作。"
fi

printf '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"%s"}}\n' "$(gg_json_escape "$msg")"
