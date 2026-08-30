#!/usr/bin/env bash
# 所有閘門檔的合併指紋。
#
# ★ 兩棵樹的指紋**本來就不同**，這是對的：.claude/settings.json 裡的
#   CLAUDE_PROJECT_DIR 後備路徑各指自己那一棵（只差那三行）。
#   不要為了讓指紋一致而去同步 settings.json——那會讓其中一棵的 hook
#   指到另一棵的檔案。每棵樹各自跑 selftest、各自蓋章。
#selftest 通過時把它寫進 .selftest-stamp，
# 稽核第 ⑩ 項比對——不一致就代表「閘門改過但沒重跑自我測試」。
gg_gate_fingerprint() {
  local root="$1"
  {
    for f in "$root"/.dispatch/lib.sh \
             "$root"/.dispatch/shell-guard-lib.sh \
             "$root"/.dispatch/audit-dispatch.sh \
             "$root"/.dispatch/check-progress.py \
             "$root"/.dispatch/selftest.sh \
             "$root"/.claude/hooks/*.sh \
             "$root"/.codex/hooks/*.sh \
             "$root"/.claude/settings.json \
             "$root"/.codex/hooks.json; do
      [ -f "$f" ] || continue
      # 忽略換行差異，否則 CRLF/LF 會讓指紋在兩棵樹之間對不上
      tr -d "\015" < "$f"
    done
  } | md5sum | cut -d' ' -f1
}
