#!/usr/bin/env bash
# UserPromptSubmit：從 prompt 裡認出 GG_PACKAGE=<包名>，綁到這個 session。
#
# 這是給「lead 用 ai-cli fan out 子 agent」那條路用的：
# ai-cli 的 run 沒有環境變數參數，所以包別只能從 prompt 帶進來。
# 綁定的檔名是 session_id，多個子 agent 同時跑不會互相蓋掉。
#
# 只認第一次宣告。已經綁過的 session 再送 GG_PACKAGE= 會被拒絕——
# 否則子 agent 只要在後續訊息裡寫一句就能自己換包，那閘門就沒有意義了。
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/.dispatch/lib.sh"

payload="$(cat)"
sid="$(gg_session_id "$payload")"
[ -n "$sid" ] || exit 0

claimed="$GG_ROOT/.dispatch/.session/$sid"
pkg="$(printf '%s' "$payload" | grep -oE 'GG_PACKAGE=[A-Za-z0-9._-]+' | head -1 | sed 's/^GG_PACKAGE=//')"
[ -n "$pkg" ] || exit 0

note() {
  printf '{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"%s"}}\n' "$(gg_json_escape "$1")"
  exit 0
}

if [ -f "$claimed" ]; then
  cur="$(cat "$claimed")"
  [ "$cur" = "$pkg" ] && exit 0
  note "這個 session 已經綁定 ${cur}，不接受改成 ${pkg}。要換包請重開一個 session。閘門仍照 ${cur} 判斷。"
fi

if ! gg_active_packages | grep -qxF "$pkg"; then
  note "prompt 宣告了 GG_PACKAGE=${pkg}，但 .dispatch/ACTIVE.md 裡沒有這一包（現有的是：$(gg_active_packages | gg_join '、')）。沒有綁定，閘門會擋下所有原始碼寫入。請向整合者確認包名。"
fi

gg_remember_package "$sid" "$pkg" || exit 0
GG_PACKAGE="$pkg" note "已綁定：這個 session 受 ${pkg} 管。授權寫入的路徑只有這些：$(GG_PACKAGE="$pkg" gg_allow_list | gg_join '　')。範圍外的原始碼一律不准寫。自驗全過就停，不要往下做別包，也不能自己宣告通過。"
