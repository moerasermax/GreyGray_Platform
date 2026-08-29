#!/usr/bin/env bash
# Codex · SessionStart：把「現在生效的派工是什麼」送進 context。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

if ! gg_has_dispatch; then
  msg="目前沒有生效中的派工（.dispatch/ACTIVE.md 沒有任何 package）。這代表沒有人派工給你：原始碼一律不准寫。要開工請先向整合者要派工書，不要自己挑一件事做。"
elif ! gg_package_valid; then
  msg="GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，直到包名對上為止。停下來確認你要做的是哪一包，不要自己改 ACTIVE.md。"
else
  msg="你受管的包是 $(gg_scope_label)。派工書：$(gg_active_docs | gg_join '、')。授權寫入的路徑只有這些：$(gg_allow_list | gg_join '　')。這個範圍以外的原始碼一律不准寫，apply_patch 會被閘門擋下、收工前 git diff 也會再查一次。需要範圍外的檔案就停下來回報，不要自己改。自驗全過之後就停，不要往下做下一包，也不要越波次——總驗收是整合者的工作，交付方不能自己宣告通過。"
  if gg_scope_is_loose; then
    msg="${msg} 注意：現在同時有多包生效而這個 session 沒有設 GG_PACKAGE，閘門只擋得住「整波之外」，擋不住你去寫別包的檔案。請照派工書的所有權表自律。"
  fi
fi

printf '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"%s"}}\n' "$(gg_json_escape "$msg")"
