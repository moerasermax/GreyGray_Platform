#!/usr/bin/env bash
# Claude Code · SessionStart：把「現在生效的派工是什麼」直接送進 context。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

if ! gg_has_dispatch; then
  msg="目前沒有生效中的派工（.dispatch/ACTIVE.md 沒有任何 package）。這代表現在是整合者模式：原始碼一律不准寫，只能動 docs/、management/、閘門自己的檔案與 PM 資料夾。要動原始碼就先在 ACTIVE.md 開一筆 package，留下軌跡。"
elif ! gg_package_valid; then
  msg="GG_PACKAGE 設成了 ${GG_PACKAGE}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。閘門會擋下所有原始碼寫入，直到包名對上為止。停下來確認你要做的是哪一包，不要自己改 ACTIVE.md。"
else
  msg="你受管的包是 $(gg_scope_label)。派工書：$(gg_active_docs | gg_join '、')。授權寫入的路徑只有這些：$(gg_allow_list | gg_join '　')。這個範圍以外的原始碼一律不准寫，PreToolUse 閘門會擋、收工前 git diff 也會再查一次。需要範圍外的檔案就停下來回報，不要自己改。自驗全過之後就停，不要往下做下一包——整合驗收不是你的工作。"
  if gg_scope_is_loose; then
    msg="${msg} 注意：現在同時有多包生效而這個 session 沒有設 GG_PACKAGE，閘門只擋得住「整波之外」，擋不住你去寫別包的檔案。請照派工書的所有權表自律，或請整合者用 GG_PACKAGE=<包名> 重開這個 session。"
  fi
fi

printf '{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"%s"}}\n' "$(gg_json_escape "$msg")"
