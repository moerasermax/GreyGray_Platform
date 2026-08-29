#!/usr/bin/env bash
# 派工閘門的共用邏輯。**agent 中立**：Claude Code 與 Codex 的 hook 都 source 這一份，
# 讀的也是同一份 .dispatch/ACTIVE.md。
#
# 放在 .dispatch/ 而不是 .claude/ 或 .codex/ 底下是刻意的：
# 派工狀態只能有一份。兩個 agent 各自維護一份的話，就會變成
# STATE.md 在兩棵 worktree 上分岔那種故障——在不同視窗看到不同的「現況」。
#
# ACTIVE.md 格式：
#
#   package: FE-9
#   doc: docs/12-前端第三波派工書.md
#   allow: frontend/packages/api-client/src/types.admin.ts
#
# 沒有任何 package: 行 → 整合者模式（不是實作者），原始碼一律不准寫。
# 整合者自己要動原始碼時，就在 ACTIVE.md 開一筆 package，留下軌跡。

set -u

# repo 根目錄：本檔在 <root>/.dispatch/
GG_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GG_ACTIVE="$GG_ROOT/.dispatch/ACTIVE.md"

# 不受派工限制的路徑（整合者與 PM 的工作，不是「開工」）
GG_ALWAYS_ALLOW="docs/ management/ STATE.md README.md CLAUDE.md AGENTS.md"

# 閘門自己的檔案。**只有整合者模式能寫。**
# 這一條是刻意的：如果實作者能改 ACTIVE.md，他就能自己擴大授權範圍，
# 那閘門只是建議，不是閘門。
GG_GATE_PATHS=".dispatch/ .claude/ .codex/"

# 把任意路徑正規化成「相對 repo 根、正斜線」的形式。
gg_relpath() {
  # 反斜線一律轉正斜線，再把連續斜線壓成一個。
  # 不要用 ${p//\\\\//} 那種寫法：JSON 逃脫的 \\ 會被換成 // ，之後比對就永遠對不上，
  # 然後掉進「repo 外的絕對路徑 → 放行」那條分支——那是個會讓整個閘門失效的 fail-open。
  local p root lp lr
  p="$(printf '%s' "$1" | tr '\\' '/' | sed 's|//*|/|g')"
  root="$(printf '%s' "$GG_ROOT" | tr '\\' '/' | sed 's|//*|/|g')"
  # 兩邊可能是不同形式：工具給的是 D:/foo，$PWD 在 Git Bash 是 /d/foo。
  # 統一成 d:/foo 再比。`/d/` 與 `d:/` 都是三個字元，長度不變，
  # 所以下面用 ${#root} 去切原字串仍然對得上。
  lp="$(printf '%s' "$p"    | sed -E 's|^/([A-Za-z])/|\1:/|' | tr 'A-Z' 'a-z')"
  lr="$(printf '%s' "$root" | sed -E 's|^/([A-Za-z])/|\1:/|' | tr 'A-Z' 'a-z')"
  case "$lp" in
    "$lr"/*) printf '%s' "${p:${#root}+1}" ;;
    "$lr")   printf '%s' "." ;;
    *)       printf '%s' "$p" ;;
  esac
}

# ACTIVE.md 的「活體」內容：剔除 ``` 圍欄裡的格式範例，以及 <!-- --> 註解掉的區塊。
# 這一步不能省——註解掉的範例如果被當成生效中的派工讀進來，閘門會整個失效。
gg_active_body() {
  [ -f "$GG_ACTIVE" ] || return 0
  awk '
    BEGIN { fence = 0; comment = 0 }
    /^[[:space:]]*```/ { fence = !fence; next }
    fence { next }
    comment { if (index($0, "-->")) comment = 0; next }
    index($0, "<!--") { if (!index($0, "-->")) comment = 1; next }
    { print }
  ' "$GG_ACTIVE"
}

# 只取某一包的 allow 清單。$1 空字串 = 全部包的聯集。
#
# 為什麼需要分包：三個 agent 同時跑時，聯集會讓 FE-9 有權寫 FE-10 的檔案，
# 而「同一波裡兩個平行 agent 動到同一個檔案」正是所有權表要防的事。
# 每個 agent 開工前設 GG_PACKAGE=<包名>，閘門就只認那一包的路徑。
gg_block_allows() {
  gg_active_body | awk -v want="$1" '
    /^[[:space:]]*package:/ {
      cur = $0
      sub(/^[[:space:]]*package:[[:space:]]*/, "", cur)
      sub(/[[:space:]]*$/, "", cur)
      next
    }
    /^[[:space:]]*allow:/ {
      if (want == "" || want == cur) {
        line = $0
        sub(/^[[:space:]]*allow:[[:space:]]*/, "", line)
        sub(/[[:space:]]*$/, "", line)
        if (line != "") print line
      }
    }
  '
}

gg_allow_list() {
  gg_block_allows "${GG_PACKAGE:-}"
}

# GG_PACKAGE 有設、但 ACTIVE.md 裡沒有這一包 → 回傳非 0。
# 這種情況要 fail-closed：拼錯包名不該變成「什麼都能寫」。
gg_package_valid() {
  [ -n "${GG_PACKAGE:-}" ] || return 0
  gg_active_packages | grep -qxF "$GG_PACKAGE"
}

gg_active_packages() {
  gg_active_body | sed -n 's/^[[:space:]]*package:[[:space:]]*//p' | sed 's/[[:space:]]*$//' | grep -v '^$'
}

# 派工書路徑。同樣依 GG_PACKAGE 分包，並去重（多包指向同一份派工書是常態）。
gg_active_docs() {
  gg_active_body | awk -v want="${GG_PACKAGE:-}" '
    /^[[:space:]]*package:/ {
      cur = $0
      sub(/^[[:space:]]*package:[[:space:]]*/, "", cur)
      sub(/[[:space:]]*$/, "", cur)
      next
    }
    /^[[:space:]]*doc:/ {
      if (want == "" || want == cur) {
        line = $0
        sub(/^[[:space:]]*doc:[[:space:]]*/, "", line)
        sub(/[[:space:]]*$/, "", line)
        if (line != "" && !seen[line]++) print line
      }
    }
  '
}

# 把多行併成一行。tr 是位元組導向的，中文分隔符會被切壞，所以用 sed。
gg_join() {
  sed ":a;N;\$!ba;s/\n/$1/g"
}

gg_has_dispatch() {
  [ -n "$(gg_active_packages)" ]
}

# ── 包別的來源有兩個 ───────────────────────────────────────────────
# 1. 環境變數 GG_PACKAGE —— 人自己開 terminal 時用，最直接
# 2. session 標記 .dispatch/.session/<session_id> —— 給 ai-cli 派出去的子 agent 用
#
# 之所以需要第 2 種：ai-cli 的 run 只吃 workFolder／prompt／model，
# **沒有環境變數參數**，所以 lead fan out 子 agent 時 GG_PACKAGE 傳不進去。
# 但 hook payload 一定帶 session_id，於是改成：lead 在子 agent 的 prompt 裡寫
# GG_PACKAGE=FE-9，UserPromptSubmit 認出來後把包別綁到那個 session_id 上。
# 多個子 agent 同時跑也不會互相蓋掉，因為檔名就是各自的 session_id。

gg_session_id() {
  printf '%s' "$1" | sed -n 's/.*"session_id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1
}

# 把包別綁到某個 session
gg_remember_package() {
  local sid="$1" pkg="$2"
  [ -n "$sid" ] && [ -n "$pkg" ] || return 1
  mkdir -p "$GG_ROOT/.dispatch/.session" 2>/dev/null || return 1
  printf '%s' "$pkg" > "$GG_ROOT/.dispatch/.session/$sid"
}

# 決定這個 session 受哪一包管。環境變數優先，其次才是 session 標記。
gg_resolve_package() {
  [ -z "${GG_PACKAGE:-}" ] || return 0
  local sid f
  sid="$(gg_session_id "${1:-}")"
  [ -n "$sid" ] || return 0
  f="$GG_ROOT/.dispatch/.session/$sid"
  if [ -f "$f" ]; then
    GG_PACKAGE="$(cat "$f")"
    export GG_PACKAGE
  fi
  return 0
}

# 這個 session 實際受哪一包管：有設 GG_PACKAGE 就是它，沒設就是全部（聯集）
gg_scope_label() {
  if [ -n "${GG_PACKAGE:-}" ]; then
    printf '%s' "$GG_PACKAGE"
  else
    gg_active_packages | gg_join '、'
  fi
}

# 同時有多包生效、卻沒指定 GG_PACKAGE → 閘門只擋得住「整波之外」，
# 擋不住包與包之間互相越界。回傳 0 代表處在這個較弱的狀態。
gg_scope_is_loose() {
  [ -z "${GG_PACKAGE:-}" ] && [ "$(gg_active_packages | grep -c '')" -gt 1 ]
}

# 判斷一個路徑是否被允許寫入。0 = 允許，1 = 不允許
gg_path_allowed() {
  local rel="$1" prefix
  # repo 之外的路徑不歸這個閘門管（例如 GreyGray_PM、暫存區）
  case "$rel" in
    /*|?:/*) return 0 ;;
  esac
  for prefix in $GG_ALWAYS_ALLOW; do
    case "$rel" in "$prefix"*) return 0 ;; esac
  done
  # 閘門自己的檔案：只有「沒有綁定包別」的 session 才放行。
  #
  # 原本這裡寫的是 `! gg_has_dispatch`（＝完全沒有派工生效時才放行），
  # 但那把兩件不同的事混在一起了：整合者需要維護閘門的時機，
  # 剛好就是有派工生效的時候（要加一包、要改 PROMPTS、驗收完要撤一包）。
  # 照舊寫法，一旦派下去閘門就變成沒人能維護。
  #
  # 現在的判準是「這個 session 有沒有綁到包別」：
  # 實作者一定有（PROMPTS.md 的每份 prompt 都帶 GG_PACKAGE=，
  # claim-package.sh 會把它綁到 session_id），所以實作者仍然改不了閘門，
  # 自我擴權那條路還是堵死的。
  #
  # 殘留風險誠實寫著：實作者若「完全沒宣告包別」就能寫閘門檔。
  # 那個狀態本身已經是降級狀態（gg_scope_is_loose），SessionStart 會明講，
  # 而且子代理的 prompt 一律帶 GG_PACKAGE=，實務上不會落到那裡。
  if [ -z "${GG_PACKAGE:-}" ]; then
    for prefix in $GG_GATE_PATHS; do
      case "$rel" in "$prefix"*) return 0 ;; esac
    done
  fi
  local allowed=1
  while IFS= read -r prefix; do
    [ -n "$prefix" ] || continue
    case "$rel" in "$prefix"*) allowed=0; break ;; esac
  done <<EOF
$(gg_allow_list)
EOF
  return $allowed
}

# 從 hook payload 抽出所有「要被寫入的檔案路徑」。
# Claude Code 給的是 tool_input.file_path；
# Codex 主要走 apply_patch，路徑藏在 patch 內文的 *** Add/Update/Delete File: 標記裡。
# 兩種都要抓，否則 Codex 那邊等於沒有閘門。
gg_extract_paths() {
  local payload="$1"
  {
    printf '%s' "$payload" | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
    printf '%s' "$payload" | grep -oE '\*\*\* (Add|Update|Delete) File: [^"\\]+' \
      | sed -E 's/^\*\*\* (Add|Update|Delete) File: //'
  } | sed 's/[[:space:]]*$//' | grep -v '^$' | sort -u
}

# 把字串裡對 JSON 有意義的字元逃脫掉
gg_json_escape() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr '\n' ' '
}

# 收工前的越界檢查（Claude 與 Codex 共用）。印出越界的檔案清單，沒有就不印。
# core.quotepath=false 是必要的：預設 git 會把中文檔名輸出成 "\345\211..." 這種
# 加引號的八進位逃脫，前綴比對會整個對不中，於是每個中文檔名都被誤報成越界。
gg_out_of_scope_files() {
  command -v git >/dev/null 2>&1 || return 0
  git -C "$GG_ROOT" rev-parse --git-dir >/dev/null 2>&1 || return 0
  local changed f
  changed="$(
    { git -C "$GG_ROOT" -c core.quotepath=false diff --name-only HEAD 2>/dev/null
      git -C "$GG_ROOT" -c core.quotepath=false ls-files --others --exclude-standard 2>/dev/null
    } | sort -u | grep -v '^$'
  )"
  [ -n "$changed" ] || return 0
  # 用「整波聯集」判斷，不是用自己那一包。
  #
  # 同一棵 worktree 裡多個 agent 平行跑時，git diff 看得到別人的交付，
  # 但看不出那是誰寫的。拿自己那一包去判，別人的正當交付會被誤報成你的越界——
  # 實測過：FE-9 交付後還沒提交，FE-10 一收工就被自己的閘門擋住。
  #
  # 精確到「包」的把關由 PreToolUse 負責，那一層知道是誰在寫。
  # 這一層只負責攔「整波之外」，也就是用 shell 繞過 PreToolUse 的那種寫入。
  (
    unset GG_PACKAGE
    while IFS= read -r f; do
      [ -n "$f" ] || continue
      gg_path_allowed "$f" || printf '%s\n' "$f"
    done <<EOF
$changed
EOF
  )
}

# 整合者模式的 PM 同步檢查。有問題就印出訊息，沒有就不印。
# 每一個生效中的包，.dispatch/PROMPTS.md 裡都要有可以直接貼的啟動 prompt。
#
# 為什麼要擋：派工書寫完、ACTIVE.md 也啟用了，但沒有人整理出「這一包怎麼開」，
# 使用者就得自己回去讀派工書再拼一段 prompt 出來——那是整合者漏做的一步。
# 回傳缺少 prompt 的包名（以、分隔），全部齊了就回空字串。
gg_prompts_missing() {
  local f="$GG_ROOT/.dispatch/PROMPTS.md" pkg missing=""
  gg_has_dispatch || return 0
  if [ ! -f "$f" ]; then
    gg_active_packages | gg_join '、'
    return 0
  fi
  while IFS= read -r pkg; do
    [ -n "$pkg" ] || continue
    # 要精確比對，否則 BE-1 會被 BE-13 誤判成已涵蓋
    grep -qE "GG_PACKAGE=${pkg}([[:space:]]|\$)" "$f"       || missing="${missing}${missing:+、}${pkg}"
  done <<EOF
$(gg_active_packages)
EOF
  printf '%s' "$missing"
}

gg_pm_out_of_sync() {
  local pm="$GG_ROOT/../GreyGray_PM"
  [ -d "$pm" ] || return 0
  local record="$pm/03-驗收紀錄.md" master="$pm/00-進度總表.md" board="$pm/web/dashboard.html"
  local msgs=""
  [ -f "$record" ] && [ -f "$master" ] && [ "$record" -nt "$master" ] && \
    msgs="$msgs 03-驗收紀錄.md 比 00-進度總表.md 新：驗收寫了，進度總表還沒更新。"
  [ -f "$master" ] && [ -f "$board" ] && [ "$master" -nt "$board" ] && \
    msgs="$msgs 00-進度總表.md 比 web/dashboard.html 新：儀表板的 DATA 還沒跟上（PM README 更新規則第 3 條）。"
  [ -n "$msgs" ] && printf '%s' "$msgs"
  return 0
}
