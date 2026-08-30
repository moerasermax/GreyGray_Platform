#!/usr/bin/env bash
# 閘門自我測試：窮舉「身分 × 工具 × 路徑類別 × 指令類別」，不是抽查。
#
# 為什麼要有這支：前兩次檢查都是「再看一遍」，所以每次都還能再找到新東西
# （第一次 3 個、第二次 8 個）。靠眼睛掃不會收斂，靠窮舉才會。
# 這支是常駐的——以後改任何閘門檔，先跑它。
#
# 用法：bash .dispatch/selftest.sh [--agent claude|codex]
set -u
GG_SELFTEST_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
. "$GG_SELFTEST_ROOT/.dispatch/lib.sh"

AGENT=claude
[ "${1:-}" = "--agent" ] && AGENT="${2:-claude}"
GUARD=".$([ "$AGENT" = codex ] && echo codex || echo claude)/hooks/dispatch-guard.sh"
STOPG=".$([ "$AGENT" = codex ] && echo codex || echo claude)/hooks/stop-gate.sh"
CLAIM=".$([ "$AGENT" = codex ] && echo codex || echo claude)/hooks/claim-package.sh"
BRIEF=".$([ "$AGENT" = codex ] && echo codex || echo claude)/hooks/session-brief.sh"

PASS=0; FAIL=0; FAILED=""
ok_()   { PASS=$((PASS+1)); }
bad_()  { FAIL=$((FAIL+1)); FAILED="$FAILED
  $1"; printf '  FAIL %s\n' "$1"; }

# 判斷一次 guard 呼叫的結果
verdict() { case "$1" in *'"permissionDecision":"deny"'*|*'"decision":"deny"'*) echo deny ;; *) echo allow ;; esac; }

t_guard() { # t_guard <期望> <身分env> <payload> <描述>
  local want="$1" idenv="$2" pl="$3" desc="$4" out got
  out="$(printf '%s' "$pl" | env -u GG_PACKAGE -u GG_ROLE -u GG_UNION $idenv bash "$GG_SELFTEST_ROOT/$GUARD" 2>/dev/null)"
  got="$(verdict "$out")"
  if [ "$got" = "$want" ]; then ok_; else bad_ "[$AGENT] $desc → 期望 $want 實得 $got"; fi
}

pl_write() { printf '{"session_id":"st","tool_name":"%s","tool_input":{"%s":"%s","content":"x"}}' "$1" "$3" "$2"; }
pl_cmd()   { printf '{"session_id":"st","tool_name":"Bash","tool_input":{"command":"%s"}}' "$1"; }

# ── 路徑類別 ────────────────────────────────────────────────────
PKG_A="$(gg_active_packages | sed -n 1p)"
PKG_B="$(gg_active_packages | sed -n 2p)"

# ★ 整合者模式（零個生效包）跑不了這份矩陣，而且要明講。
#   這裡的每一段都以「有一個生效包」為前提——PKG_A 是空的時候，
#   140 條斷言會全部拿空字串去比，跑出一整片無意義的紅字，
#   其中還包含「ACTIVE.md 沒還原」這種會讓人以為檔案壞掉的假警報（實際上好好的）。
#   ★ 不可以改成「安靜地通過」：那就變成這支測試自己在示範它要抓的那個病
#     ——查了零個對象，看起來跟查過都沒事一模一樣。
if [ -z "$PKG_A" ]; then
  echo "═══ 自我測試（agent=$AGENT，樹=$(basename "$GG_SELFTEST_ROOT")）═══"
  echo "  ⚠ 現在是整合者模式（.dispatch/ACTIVE.md 沒有任何生效的 package），"
  echo "    這份矩陣的每一段都需要一個生效包才有意義，所以**沒有跑**。"
  echo "    這不是通過，也不是失敗——是沒東西可測。"
  echo "    有派工生效時再跑；蓋章檔維持原狀，稽核第 ⑩ 項在整合者模式下不會被查到。"
  exit 0
fi
OWN="$(GG_PACKAGE="$PKG_A" gg_allow_list | grep -v '^tests/$' | sed -n 1p)"
[ -n "$OWN" ] || OWN="src/"

# ★ 「別包的路徑」不能拿 PKG_B 的 allow 來當——只有一個生效包時 PKG_B 是空的，
#   而 gg_allow_list 對空包別會退回**聯集**，於是抓到的其實是 PKG_A 自己的路徑，
#   測試就會自己騙自己（前端樹只有一包時實測踩到）。
#   改用保證在所有 allow 之外的合成路徑，不管幾個包都成立。
OUTSIDE="src/__selftest_outside__/"
if gg_path_allowed "${OUTSIDE}x.cs" 2>/dev/null; then
  echo "  ✗ 合成路徑 $OUTSIDE 竟然被允許，測試前提不成立"; exit 1
fi
# 真的有第二個包時，額外測「跨包」；沒有就跳過（而不是假裝測了）
OTHER=""
if [ -n "$PKG_B" ]; then
  OTHER="$(GG_PACKAGE="$PKG_B" gg_allow_list | grep -v '^tests/$' | sed -n 1p)"
fi

echo "═══ 自我測試（agent=$AGENT，樹=$(basename "$GG_SELFTEST_ROOT")）═══"
echo "  生效包：$(gg_active_packages | gg_join '、')　樣本 A=$PKG_A B=$PKG_B"

# ══ 1a. 路徑類別 × 身分（用 Write 跑一次就夠）════════════════
#
# 路徑判斷在 gg_path_antfin… 不，在 gg_path_allowed，與是哪個工具無關，
# 所以不必每個工具都跑全套路徑——那只是把同一條分支跑四遍，
# 換來的是跑不完。工具是否被認出來另外驗（1b）。
#
# 這張表就是閘門的規格。寫在這裡才是可執行的規格。
echo "── 1a. 路徑類別 × 身分 ──"
while IFS='|' read -r d p e_impl e_lead e_unk; do
  [ -n "$d" ] || continue
  t_guard "$e_impl" "GG_PACKAGE=$PKG_A" "$(pl_write Write "$p" file_path)" "實作者 $d"
  t_guard "$e_lead" "GG_ROLE=leader"    "$(pl_write Write "$p" file_path)" "Leader $d"
  t_guard "$e_unk"  ""                  "$(pl_write Write "$p" file_path)" "身分不明 $d"
  t_guard deny "GG_PACKAGE=NO-SUCH-PKG" "$(pl_write Write "$p" file_path)" "包名拼錯 $d"
done <<EOF
自己 allow 內|${OWN}zz.tmp|allow|deny|deny
所有 allow 之外|${OUTSIDE}zz.tmp|deny|deny|deny
全域放行 docs/|docs/zz.tmp|allow|allow|allow
閘門檔 ACTIVE.md|.dispatch/ACTIVE.md|deny|allow|deny
閘門檔 settings|.claude/settings.json|deny|allow|deny
自己的自驗報告|.dispatch/reports/${PKG_A}.md|allow|allow|deny
別包的自驗報告|.dispatch/reports/${PKG_B}.md|deny|allow|deny
不存在包的報告|.dispatch/reports/NOPE.md|deny|allow|deny
報告遍歷變形|.dispatch/reports/${PKG_A}.md/../ACTIVE.md|deny|allow|deny
報告後綴變形|.dispatch/reports/${PKG_A}.md.bak|deny|allow|deny
未授權原始碼|src/Zzz/Nope.cs|deny|deny|deny
EOF

# ══ 1a-2. 跨包越界（只有 ≥2 個生效包時才測，否則明講跳過）═════
if [ -n "$OTHER" ]; then
  echo "── 1a-2. 跨包越界 ──"
  t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write "${OTHER}zz.tmp" file_path)" "實作者寫別包（$PKG_B）的路徑"
  t_guard deny "GG_PACKAGE=$PKG_B" "$(pl_write Write "${OWN}zz.tmp" file_path)"   "反向：別包寫本包的路徑"
else
  echo "── 1a-2. 跨包越界：只有一個生效包，跳過（不是通過）──"
fi

# ══ 1b. 每個寫檔工具都要被認出來 ════════════════════════════════
# 用「實作者寫別包路徑」當探針：認得出來就會 deny，認不出來就漏放行。
echo "── 1b. 寫檔工具辨識 ──"
for tf in "Write:file_path" "Edit:file_path" "MultiEdit:file_path" "NotebookEdit:notebook_path" "apply_patch:file_path"; do
  TOOL="${tf%%:*}"; FIELD="${tf##*:}"
  [ "$AGENT" = claude ] && [ "$TOOL" = apply_patch ] && continue
  [ "$AGENT" = codex ]  && [ "$TOOL" = NotebookEdit ] && continue
  if [ "$TOOL" = apply_patch ]; then
    pl="$(printf '{"session_id":"st","tool_name":"apply_patch","tool_input":{"input":"*** Update File: %szz.tmp"}}' "$OUTSIDE")"
  else
    pl="$(pl_write "$TOOL" "${OUTSIDE}zz.tmp" "$FIELD")"
  fi
  t_guard deny "GG_PACKAGE=$PKG_A" "$pl" "工具 $TOOL 寫範圍外路徑要擋"
done

# ══ 2. shell 指令類別 × 身分 ════════════════════════════════════
#
# 危險指令要防的是「工作區裡別人未提交的交付」，所以與有沒有生效派工無關。
# git commit 只放行 Leader。其餘對所有人都擋，包括 Leader。
echo "── 2. shell 指令 × 身分 ──"
while IFS='|' read -r cmd e_impl e_lead e_unk; do
  [ -n "$cmd" ] || continue
  t_guard "$e_impl" "GG_PACKAGE=$PKG_A" "$(pl_cmd "$cmd")" "shell 實作者：$cmd"
  t_guard "$e_lead" "GG_ROLE=leader"    "$(pl_cmd "$cmd")" "shell Leader：$cmd"
  t_guard "$e_unk"  ""                  "$(pl_cmd "$cmd")" "shell 身分不明：$cmd"
done <<'EOF'
git diff --stat|allow|allow|allow
git status|allow|allow|allow
ls -la|allow|allow|allow
git stash|deny|deny|deny
git stash push -u -m x|deny|deny|deny
git reset --hard HEAD|deny|deny|deny
git reset  --hard|deny|deny|deny
git clean -fd|deny|deny|deny
git checkout -- .|deny|deny|deny
git checkout .|deny|deny|deny
git checkout . && echo hi|deny|deny|deny
git restore .|deny|deny|deny
git restore -- .|deny|deny|deny
git commit -m x|deny|allow|deny
cd foo && git stash|deny|deny|deny
echo safe; git clean -xfd|deny|deny|deny
EOF

# ══ 3. 畸形／邊界 payload（不能因為看不懂就放行）════════════════
echo "── 3. 畸形與邊界 payload ──"
t_guard deny "GG_PACKAGE=$PKG_A" '{"session_id":"st","tool_name":"Write","tool_input":{"file_path":"src/Zzz/A.cs","content":"x"},"extra":{"file_path":"docs/ok.md"}}' "同時含兩個 file_path，其一越界"
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write 'src/Zzz/含中文 空格.cs' file_path)" "路徑含中文與空格"
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write 'src\Zzz\Backslash.cs' file_path)" "Windows 反斜線路徑"
t_guard allow "GG_PACKAGE=$PKG_A" '{"session_id":"st","tool_name":"Read","tool_input":{"file_path":"src/Zzz/A.cs"}}' "唯讀工具不該被擋"
t_guard allow "GG_PACKAGE=$PKG_A" '{}' "空 payload 不該誤擋"
t_guard allow "GG_PACKAGE=$PKG_A" 'not json at all' "非 JSON 不該誤擋"

# ══ 3b. 路徑正規化（gg_relpath 曾經在這裡 fail-open）═══════════
#
# 歷史：`${p//\//}` 把跳脫過的反斜線換成兩個斜線，比對永遠對不上，
# 於是掉進「repo 外的絕對路徑 → 放行」那條分支——整個閘門失效而且沒有訊息。
# 這一段把各種形式都跑一次。
echo "── 3b. 路徑正規化 ──"
# GG_SELFTEST_ROOT 本來就是正斜線形式，不需要 tr 轉換——
# 第一版加了 tr 反而因為跳脫被吃掉而組出 repo 之外的路徑，
# 而 repo 外本來就不歸這個閘門管，於是測試自己製造了一個假的 FAIL。
ABS_IN="${GG_SELFTEST_ROOT}/${OUTSIDE}zz.tmp"
# 用純參數展開組 Windows 形式，不碰 sed 也不碰反斜線——
# 第一版用 sed 的  反向參照，被層層跳脫吃成控制字元，
# 組出 <0x01>:/... 這種東西，而它剛好符合 ?:/ 的「repo 外」判斷 → 假的 FAIL。
_drv="${ABS_IN#/}"; _drv="${_drv%%/*}"
_rest="${ABS_IN#/${_drv}/}"
ABS_IN_WIN="${_drv}:/${_rest}"
# 前提檢查：這兩個形式都必須真的落在 repo 內，否則測的不是我們想測的東西
if [ "$(gg_relpath "$ABS_IN")" = "$ABS_IN" ] || [ "$(gg_relpath "$ABS_IN_WIN")" = "$ABS_IN_WIN" ]; then
  bad_ "[$AGENT] 路徑正規化的測試前提不成立：絕對路徑沒被轉成相對路徑"
fi
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write "$ABS_IN" file_path)"     "絕對路徑（MSYS 形式）指到範圍外"
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write "$ABS_IN_WIN" file_path)" "絕對路徑（Windows 形式）指到範圍外"
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write "./${OUTSIDE}zz.tmp" file_path)" "相對路徑帶 ./"
t_guard deny "GG_PACKAGE=$PKG_A" "$(pl_write Write "${OUTSIDE}//zz.tmp" file_path)"  "重複斜線"
# repo 之外的路徑不歸這個閘門管（例如 GreyGray_PM），應放行
t_guard allow "GG_PACKAGE=$PKG_A" "$(pl_write Write "D:/WorkSpace/01_開發中_wip/GreyGray_PM/x.md" file_path)" "repo 之外的路徑不歸這裡管"

# ══ 4. Stop 閘門 ════════════════════════════════════════════════
echo "── 4. Stop 閘門 ──"
t_stop() { # t_stop <期望 block|pass> <身分> <描述>
  local want="$1" idenv="$2" desc="$3" out got
  out="$(printf '{"session_id":"st","stop_hook_active":false}' \
        | env -u GG_PACKAGE -u GG_ROLE -u GG_UNION $idenv bash "$GG_SELFTEST_ROOT/$STOPG" 2>/dev/null)"
  case "$out" in *'"decision":"block"'*|*'"decision":"deny"'*) got=block ;; *) got=pass ;; esac
  if [ "$got" = "$want" ]; then ok_; else bad_ "[$AGENT] Stop $desc → 期望 $want 實得 $got"; fi
}
t_stop pass "GG_PACKAGE=$PKG_A -e stop_hook_active" "無限迴圈防護" 2>/dev/null || true
out_loop="$(printf '{"session_id":"st","stop_hook_active":true}' | env -u GG_PACKAGE GG_PACKAGE="$PKG_A" bash "$GG_SELFTEST_ROOT/$STOPG" 2>/dev/null)"
if [ -z "$out_loop" ]; then ok_; else bad_ "[$AGENT] Stop stop_hook_active=true 應直接放行"; fi

# ══ 5. claim-package：包別優先於角色、拼錯擋下、不得改綁 ════════
echo "── 5. claim-package ──"
t_claim() { # t_claim <sid> <prompt> <期望 pkg 檔內容或 -> <期望 role 檔內容或 ->
  local sid="$1" prompt="$2" wpkg="$3" wrole="$4" f r
  printf '{"session_id":"%s","prompt":"%s"}' "$sid" "$prompt" \
    | bash "$GG_SELFTEST_ROOT/$CLAIM" >/dev/null 2>&1
  f="$GG_SELFTEST_ROOT/.dispatch/.session/$sid"
  r="$f.role"
  local gp="-" gr="-"
  [ -f "$f" ] && gp="$(cat "$f")"
  [ -f "$r" ] && gr="$(cat "$r")"
  if [ "$gp" = "$wpkg" ] && [ "$gr" = "$wrole" ]; then ok_
  else bad_ "[$AGENT] claim $sid → 期望 pkg=$wpkg role=$wrole 實得 pkg=$gp role=$gr"; fi
  rm -f "$f" "$r"
}
t_claim st1 "GG_PACKAGE=$PKG_A"                    "$PKG_A" "-"
t_claim st2 "GG_ROLE=leader"                       "-"      "leader"
t_claim st3 "GG_ROLE=leader\nGG_PACKAGE=$PKG_A"    "$PKG_A" "-"
t_claim st4 "GG_PACKAGE=NO-SUCH"                   "-"      "-"
t_claim st5 "沒有任何宣告"                          "-"      "-"

# ══ 6. Codex 走 argv 而非 stdin 的那條路徑 ══════════════════════
if [ "$AGENT" = codex ]; then
  echo "── 6. Codex argv payload ──"
  o="$(env -u GG_PACKAGE -u GG_ROLE GG_PACKAGE="$PKG_A" bash "$GG_SELFTEST_ROOT/$GUARD"         "$(pl_write Write "${OUTSIDE}zz.tmp" file_path)" 2>/dev/null)"
  [ "$(verdict "$o")" = deny ] && ok_ || bad_ "[codex] argv 傳 payload：越界應擋"
  o="$(env -u GG_PACKAGE -u GG_ROLE GG_PACKAGE="$PKG_A" bash "$GG_SELFTEST_ROOT/$GUARD"         "$(pl_write Write "${OWN}zz.tmp" file_path)" 2>/dev/null)"
  [ "$(verdict "$o")" = allow ] && ok_ || bad_ "[codex] argv 傳 payload：自己的路徑應放行"
fi

# ══ 7. 整合者模式（ACTIVE.md 零個包）════════════════════════════
#
# 這是完全不同的一組分支：gg_has_dispatch 為假時，
# 閘門檔對「沒綁包別」放行、原始碼對所有人擋。
# 前面 109 項全部跑在「有派工」下，這一段才會走到另一半。
echo "── 7. 整合者模式（零個生效包）──"
BAK="$(mktemp)"; cp "$GG_SELFTEST_ROOT/.dispatch/ACTIVE.md" "$BAK"
python - "$GG_SELFTEST_ROOT/.dispatch/ACTIVE.md" <<'PYEOF'
import io, re, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
# 把所有生效的 package:／allow:／doc: 行註解掉
s = re.sub(r'(?m)^(package:|allow:|doc:)', r'#SELFTEST# ', s)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
PYEOF
if [ "$(gg_active_packages | grep -c .)" -ne 0 ]; then
  bad_ "[$AGENT] 整合者模式沒切成功（仍有生效包）"
else
  t_guard deny  "GG_PACKAGE=$PKG_A" "$(pl_write Write "${OWN}zz.tmp" file_path)" "整合者模式：實作者寫原始碼要擋"
  t_guard deny  ""                  "$(pl_write Write "${OWN}zz.tmp" file_path)" "整合者模式：無身分寫原始碼要擋"
  t_guard allow ""                  "$(pl_write Write '.dispatch/ACTIVE.md' file_path)" "整合者模式：無身分可維護閘門檔"
  t_guard allow "GG_ROLE=leader"    "$(pl_write Write '.dispatch/ACTIVE.md' file_path)" "整合者模式：Leader 可維護閘門檔"
  t_guard allow ""                  "$(pl_write Write 'docs/x.md' file_path)" "整合者模式：docs 仍放行"
  t_guard deny  ""                  "$(pl_cmd 'git stash')" "整合者模式：git stash 仍要擋"
  t_guard deny  ""                  "$(pl_cmd 'git clean -fd')" "整合者模式：git clean 仍要擋"
  t_guard allow "GG_ROLE=leader"    "$(pl_cmd 'git commit -m x')" "整合者模式：Leader 可 commit"
  t_guard deny  ""                  "$(pl_cmd 'git commit -m x')" "整合者模式：無身分不可 commit"
fi
cp "$BAK" "$GG_SELFTEST_ROOT/.dispatch/ACTIVE.md"; rm -f "$BAK"
[ "$(gg_active_packages | grep -c .)" -gt 0 ] && ok_ || bad_ "[$AGENT] ACTIVE.md 沒還原"

# ══ 8. session-brief 四種身分各自講對話 ═════════════════════════
echo "── 8. session-brief ──"
t_brief() { # t_brief <身分env> <期望關鍵字> <描述>
  local idenv="$1" kw="$2" desc="$3" out
  out="$(printf '{"session_id":"stb"}' | env -u GG_PACKAGE -u GG_ROLE $idenv bash "$GG_SELFTEST_ROOT/$BRIEF" 2>/dev/null)"
  case "$out" in *"$kw"*) ok_ ;; *) bad_ "[$AGENT] brief $desc 沒提到「$kw」" ;; esac
}
t_brief "GG_ROLE=leader"      "Leader"       "Leader"
t_brief "GG_PACKAGE=$PKG_A"   "$PKG_A"       "實作者"
t_brief ""                    "沒有宣告身分" "身分不明"
t_brief "GG_PACKAGE=NO-SUCH"  "沒有這一包"   "包名拼錯"

echo "── 9. 被註解掉的包、角色記憶、PM 同步 ──"
# 這三塊本來不在矩陣裡，是這一輪手驗過才發現沒被涵蓋的。
# 手驗過就要寫進來，否則下一次還是靠「我記得要看」——那從來沒有收斂過。

# 9a 註解掉的包（ACTIVE.md 裡寫著、但不生效）不可以發出任何權限。
#    這是實質的安全性質：排程未到的包若拿得到 allow，就等於提前開工。
CMTED=""
for p in $(grep -oE 'package: [A-Za-z0-9-]+' "$GG_SELFTEST_ROOT/.dispatch/ACTIVE.md" | sed 's/package: //' | sort -u); do
  gg_active_packages | grep -qxF "$p" || { CMTED="$p"; break; }
done
if [ -n "$CMTED" ]; then
  [ -z "$(GG_PACKAGE="$CMTED" gg_allow_list)" ] \
    && ok_ || bad_ "[$AGENT] 註解掉的包 $CMTED 竟然拿得到 allow"
  gg_active_packages | grep -qxF "$CMTED" \
    && bad_ "[$AGENT] 註解掉的包 $CMTED 出現在生效清單" || ok_
  t_guard deny "GG_PACKAGE=$CMTED" "$(pl_write Write "${OWN}zz.tmp" file_path)" \
          "註解掉的包 $CMTED 拿別包的路徑寫入"
else
  echo "  · ACTIVE.md 裡沒有被註解掉的包，9a 跳過（不是通過）"
fi

# 9b 包名拼錯一律 fail-closed。allow 的聯集在這裡最危險：
#    解不出包別時若退回聯集，拼錯的人會拿到整波的權限。
t_guard deny "GG_PACKAGE=NO-SUCH-PKG" "$(pl_write Write "${OWN}zz.tmp" file_path)" "包名拼錯"
t_guard deny "GG_PACKAGE=${PKG_A}x"   "$(pl_write Write "${OWN}zz.tmp" file_path)" "包名多一個字元"

# 9c 角色要記得住。Leader 只在第一則 prompt 帶 GG_ROLE=leader，
#    之後每一次工具呼叫都沒有那個 env——記憶壞掉的話 Leader 會突然變成
#    「身分不明」而被自己的閘門鎖在門外，而且看起來像閘門壞了。
#    ★ gg_resolve_role 吃的是 payload JSON（從裡面挖 session_id），不是環境變數。
_sid="st-role-$$"
gg_remember_role "$_sid" leader >/dev/null 2>&1 || true
t_guard allow "" \
  "$(printf '{"session_id":"%s","tool_name":"Write","tool_input":{"file_path":".dispatch/zz.tmp","content":"x"}}' "$_sid")" \
  "claim 過 leader 的 session，下一次呼叫沒有 env 也要認得"
t_guard deny "" \
  "$(printf '{"session_id":"st-nobody-%s","tool_name":"Write","tool_input":{"file_path":".dispatch/zz.tmp","content":"x"}}' "$$")" \
  "沒 claim 過的 session 不得寫閘門檔"
rm -f "$GG_SELFTEST_ROOT/.dispatch/.session/$_sid.role"

# 9d PM 同步檢查。用假的 GG_ROOT 跑，不去碰真的 GreyGray_PM——
#    測試不該有副作用，尤其副作用落在「單一事實來源」上。
_tmp="$(mktemp -d)"; mkdir -p "$_tmp/tree" "$_tmp/GreyGray_PM/web"
: > "$_tmp/GreyGray_PM/03-驗收紀錄.md"
: > "$_tmp/GreyGray_PM/00-進度總表.md"
: > "$_tmp/GreyGray_PM/web/dashboard.html"
touch -d '2020-01-01' "$_tmp/GreyGray_PM/03-驗收紀錄.md" "$_tmp/GreyGray_PM/00-進度總表.md" 2>/dev/null
touch "$_tmp/GreyGray_PM/web/dashboard.html"
[ -z "$(GG_ROOT="$_tmp/tree" gg_pm_out_of_sync)" ] \
  && ok_ || bad_ "[$AGENT] PM 三個檔順序正確時不該報不同步"
touch "$_tmp/GreyGray_PM/03-驗收紀錄.md"      # 驗收寫了、總表沒跟上
[ -n "$(GG_ROOT="$_tmp/tree" gg_pm_out_of_sync)" ] \
  && ok_ || bad_ "[$AGENT] 03 比 00 新，PM 同步檢查沒抓到"
rm -rf "$_tmp"

echo
echo "  ═══ 小計：通過 $PASS ／ 失敗 $FAIL ═══"
STAMP="$GG_SELFTEST_ROOT/.dispatch/.selftest-stamp"
if [ "$FAIL" -ne 0 ]; then
  echo "  失敗項目：$FAILED"
  rm -f "$STAMP"
  exit 1
fi

# 全過才蓋章。稽核第 ⑩ 項比對這個指紋——閘門檔改過而沒重跑，就對不上。
#
# 每個 agent 一行，而且只蓋自己那一行。舊版只寫一行、不記是誰跑的，
# 於是「只跑 claude」跟「兩個都跑」在稽核看起來一模一樣——
# ⑩ 的訊息寫著「兩個 agent 都要」，但它從來沒在查這件事。
. "$GG_SELFTEST_ROOT/.dispatch/gate-fingerprint.sh"
FP="$(gg_gate_fingerprint "$GG_SELFTEST_ROOT")"
TS="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
TMP="$STAMP.tmp"
[ -f "$STAMP" ] && grep -v "^${AGENT} " "$STAMP" > "$TMP" 2>/dev/null || : > "$TMP"
printf '%s %s %s %s\n' "$AGENT" "$FP" "$PASS" "$TS" >> "$TMP"
sort -o "$STAMP" "$TMP" && rm -f "$TMP"
echo "  已蓋章：.dispatch/.selftest-stamp（$AGENT）"
