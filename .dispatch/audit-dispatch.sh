#!/usr/bin/env bash
# 派工書邏輯稽核：把「我以為我查過了」變成機器查得到的東西。
#
# 為什麼要有這支：第六波的派工書寫錯三個前提（FE-15 的畫面早就做好、
# BE-14 那條牴觸凍結契約、tests/ 註記寫窄），第七波第一版又漏了一個更嚴重的——
# BE-18 要註冊事件 handler，而那個註冊檔被我劃給了 BE-19，它根本做不完。
# 這幾個錯的共同點是**都可以機械查出來**，只是我沒查。
#
# 用法：bash .dispatch/audit-dispatch.sh        （0=通過，1=有問題）
set -u
. "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

FAIL=0
NFILE="$GG_ROOT/.dispatch/.audit-n"; : > "$NFILE"; CFAIL=0
# 用 trap 清，不要在收尾手動 rm——第一版就是那樣寫的，而收尾最後一句
# say "稽核通過。" 會再把檔案建回來（say 會歸零計數），於是每跑一次
# .dispatch/ 底下就留一個未追蹤檔，看起來像有誰動了閘門。
trap 'rm -f "$NFILE"' EXIT
say()  { printf '%s
' "$1"; : > "$NFILE" 2>/dev/null || true; CFAIL=0; }
bad()  { printf '  ✗ %s
' "$1"; FAIL=1; CFAIL=1; }
ok()   { printf '  ✓ %s\n' "$1"; }

# 每一項結尾都要落一句話。「一個對象都沒查到」不是「通過」——
# ① ② 曾經在沒東西可查時完全不出聲，讀的人分不出是查過沒事、還是解析壞了。
seen() { printf x >> "$NFILE"; }

# 把派工書裡的檔案引用解成真實路徑（0 筆＝找不到，1 筆＝明確，2 筆以上＝模糊）。
# ④ 與 ⑤ 必須共用這一支：分開寫的那一版，④ 會解簡寫而 ⑤ 不會，
# 於是 ⑤ 把 Order.cs:326 這種引用整個無聲跳過——它唯一該擋的就是那種。
# 比對用 basename 全等，不用 grep -F：後者會讓 Order.cs 對到 PurchaseOrder.cs，
# 把「唯一」誤判成「模糊」。
gg_resolve_ref() {
  ( cd "$GG_ROOT" || return
    all="$(git ls-files)"
    # 先嚴：完整路徑，或以「/<引用>」結尾（路徑段全等）
    exact="$(printf '%s\n' "$all" | while IFS= read -r g; do
               case "$g" in "$1"|*/"$1") printf '%s\n' "$g" ;; esac
             done)"
    if [ -n "$exact" ]; then printf '%s\n' "$exact" | head -5; return; fi
    # 再鬆：子字串。派工書寫 Inventory.Infra/ModuleRegistration.cs 指的是
    # GreyGray.Modules.Inventory.Infra/ModuleRegistration.cs——那樣寫好讀，
    # 而且只要唯一就不算模糊。順序不能顛倒：先鬆的話 Order.cs 會對到
    # PurchaseOrder.cs，把「唯一」誤判成「模糊」。
    printf '%s\n' "$all" | grep -F -- "$1" | head -5 )
}
tally() {   # tally "<有查到且沒事時的話>" "<一項都沒查到時的話>"
  # 已經印過 ✗ 就不要再說「都沒事」；而「一項都沒查到」也不是通過——
  # ① ② 曾經在沒東西可查時完全不出聲，讀的人分不出是查過沒事、還是解析壞了。
  _n="$(wc -c < "$NFILE" | tr -d " ")"; : > "$NFILE"
  if   [ "${_n:-0}" -eq 0 ];  then printf '  ⚠ %s
' "$2"
  elif [ "$CFAIL" -eq 1 ];    then :
  else ok "$1（查了 ${_n} 項）"; fi
  CFAIL=0
}

# 這一行標明「這是還不存在的東西」，稽核就不該報它不存在
# 這一行標明「這個引用在這棵樹上本來就查不到」，稽核就不該報它。
# 兩類：① 還不存在的東西（新檔）② 存在但在另一棵 worktree（跨樹引用）。
# 豁免詞要寫在**同一行**，這是刻意的——順手加一個詞就能關掉檢查的話，
# 那個檢查遲早會被關光。
NEWMARK='新檔|新目錄|（新|尚未|還沒|不存在|待建立|另一棵樹|後端 worktree|前端 worktree|不在你這棵樹'

say "═══ 派工書邏輯稽核 ═══"

if ! gg_has_dispatch; then
  say "  沒有生效中的派工，跳過。"
  exit 0
fi

pkgs="$(gg_active_packages)"

# 解析還活著嗎。包別解不出來、或 allow 一條都解不出來的話，
# 底下每一項都會「跑完、什麼都沒查到、然後看起來像通過」——
# ① ② 就是這樣沉默了一整輪才被發現。所以先擋在這裡。
_tot=0
for pkg in $pkgs; do
  _tot=$(( _tot + $(GG_PACKAGE="$pkg" gg_allow_list | grep -c . || true) ))
done
if [ -z "$pkgs" ] || [ "$_tot" -eq 0 ]; then
  bad "ACTIVE.md 解析不出包別或 allow 前綴（包：${pkgs:-無}／前綴 $_tot 條）——底下的檢查會全部無聲通過"
  exit 1
fi
say "  （生效包：$(echo $pkgs | tr "
" " ")／allow 前綴共 $_tot 條）"

# ── 1. allow 路徑要真的存在（新檔除外）──────────────────────────
say "① allow 路徑存在性"
for pkg in $pkgs; do
  while IFS= read -r prefix; do
    [ -n "$prefix" ] || continue
    case "$prefix" in
      db/migrations/*) continue ;;   # 編號前綴，第 2 條另外查
    esac
    seen
    if [ -e "$GG_ROOT/$prefix" ]; then continue; fi
    # 目錄前綴：只要父目錄在就算合理（新子目錄）
    parent="$(dirname "$GG_ROOT/$prefix")"
    if [ -d "$parent" ]; then
      printf '  · %s 的 %s 尚不存在，但父目錄在（應為新檔／新目錄）\n' "$pkg" "$prefix"
    else
      bad "$pkg 的 allow 路徑連父目錄都不存在：$prefix"
    fi
  done <<EOF
$(GG_PACKAGE="$pkg" gg_allow_list)
EOF
done
tally "allow 路徑都存在（或父目錄在，屬新檔）" "沒有任何可查的 allow 路徑——解析可能壞了"

# ── 2. migration 編號沒被用過 ───────────────────────────────────
say "② migration 編號未被佔用"
for pkg in $pkgs; do
  while IFS= read -r prefix; do
    case "$prefix" in
      db/migrations/*)
        seen
        num="${prefix#db/migrations/}"
        # 「已被佔用」要分兩種，否則交付後必然誤判：
        #   ·  HEAD 裡就有 → 前面的波次用掉了，這一包會撞號（真的要擋）
        #   ·  只在工作區  → 這一包自己剛交付的檔（正常，撤包前一定會看到）
        #   ·  超過一個檔  → 一號多檔，任何時候都是違規
        hit="$(ls "$GG_ROOT/db/migrations/" 2>/dev/null | grep -c "^${num}" || true)"
        committed="$(git -C "$GG_ROOT" ls-tree --name-only HEAD db/migrations/ 2>/dev/null | sed 's|.*/||' | grep -c "^${num}" || true)"
        if [ "${hit:-0}" -gt 1 ]; then
          bad "$pkg 的 migration 編號 $num 有 ${hit} 個檔（一號一檔）"
        elif [ "${committed:-0}" -gt 0 ]; then
          bad "$pkg 拿到的 migration 編號 $num 在 HEAD 裡已經有檔了——前面的波次用掉了，會撞號"
        elif [ "${hit:-0}" -eq 1 ]; then
          ok "$pkg → $num 已交付（工作區一個檔，HEAD 尚無，未撞號）"
        else
          ok "$pkg → $num 未被佔用"
        fi ;;
    esac
  done <<EOF
$(GG_PACKAGE="$pkg" gg_allow_list)
EOF
done
tally "migration 編號都沒撞號" "這一波沒有任何包拿到 migration 編號（正常，但不是「查過沒事」）"

# ── 3. 兩個包不得擁有同一條 allow 前綴（tests/ 例外，已知並由驗收把關）──
say "③ 包與包之間的 allow 不重疊"
# ★ 要查的是「前綴涵蓋」不是「字串相等」。
# 這一版是修過的：第七波第一版 BE-18 拿 src/Modules/Ordering/、
# BE-19 拿 .../Ordering.Infra/ModuleRegistration.cs，兩個字串不同但實際上重疊，
# 舊的 uniq -d 完全抓不到——而那正是讓 BE-18 做不完的那個洞。
overlap=0
for a in $pkgs; do
  for b in $pkgs; do
    [ "$a" = "$b" ] && continue
    seen
    while IFS= read -r pa; do
      [ -n "$pa" ] || continue
      [ "$pa" = "tests/" ] && continue
      while IFS= read -r pb; do
        [ -n "$pb" ] || continue
        [ "$pb" = "tests/" ] && continue
        # pb 落在 pa 底下（含相等）就是重疊
        case "$pb" in
          "$pa"*)
            if [ "$pa" = "$pb" ]; then
              [ "$a" \< "$b" ] && bad "$a 與 $b 都擁有 $pa"   # 對稱，只報一次
            else
              bad "$b 的 $pb 落在 $a 的 $pa 底下——兩包會搶同一個檔"
            fi
            overlap=1 ;;
        esac
      done <<EOF
$(GG_PACKAGE="$b" gg_allow_list)
EOF
    done <<EOF
$(GG_PACKAGE="$a" gg_allow_list)
EOF
  done
done
tally "沒有重疊（tests/ 是已知的共用，由總驗收逐檔看 diff）" "只有一包生效，無從重疊——這一項這一波沒有意義"


say "④ 派工書引用的檔案存在"
docs="$(gg_active_docs | sort -u)"
for d in $docs; do
  [ -f "$GG_ROOT/$d" ] || { bad "派工書本身不存在：$d"; continue; }
  # 反引號裡、看起來像原始碼路徑的 token
  # 字元類要含括號：Next.js 的 route group 目錄長 (checkout)、(dash) 這樣，
  # 少了括號會把 (checkout)/_lib/labels.ts 切成 /_lib/labels.ts，
  # 而那個殘段對到六個檔——本來明確的引用被自己的抓取切成「模糊」。
  grep -oE '`[A-Za-z0-9_./()-]+\.(cs|ts|tsx|sql|ps1|yaml|yml|json|sh)`' "$GG_ROOT/$d" \
    | tr -d '`' | sort -u | while IFS= read -r f; do
      [ -n "$f" ] || continue
      seen
      [ -e "$GG_ROOT/$f" ] && continue
      # 那一行有沒有標明是新檔
      if grep -F -- "$f" "$GG_ROOT/$d" | grep -qE "$NEWMARK"; then continue; fi
      # 可能是相對於 frontend/ 的路徑
      [ -e "$GG_ROOT/frontend/$f" ] && continue
      # 簡寫路徑：只要能「唯一」對到一個真檔就算數。
      # 派工書寫 Ordering.Infra/ModuleRegistration.cs 比寫完整路徑好讀，
      # 但前提是它指得明確——對到兩個以上就是模糊，agent 要猜，那就是問題。
      hits="$(gg_resolve_ref "$f")"
      cnt="$(printf '%s' "$hits" | grep -c . || true)"
      if [ "${cnt:-0}" -eq 1 ]; then continue; fi
      if [ "${cnt:-0}" -gt 1 ]; then
        printf '  ✗ %s 的 %s 是模糊簡寫，對到 %s 個檔，agent 要猜
' "$d" "$f" "$cnt"
        echo "AUDIT_FAIL" >> "$GG_ROOT/.dispatch/.audit-flag"
        continue
      fi
      printf '  ✗ %s 引用了不存在的檔：%s\n' "$d" "$f"
      echo "AUDIT_FAIL" >> "$GG_ROOT/.dispatch/.audit-flag"
    done
done
if [ -f "$GG_ROOT/.dispatch/.audit-flag" ]; then FAIL=1; CFAIL=1; rm -f "$GG_ROOT/.dispatch/.audit-flag"; fi
tally "沒有引用不存在的檔" "派工書裡一個檔案引用都沒抓到——抓取用的正規表示式可能失效了"

# ── 5. `檔案:行號` 的引用，行數要夠 ─────────────────────────────
say "⑤ 行號引用不超出檔案長度"
for d in $docs; do
  [ -f "$GG_ROOT/$d" ] || continue
  grep -oE '`?[A-Za-z0-9_./()-]+\.(cs|ts|tsx|sql|ps1|yaml)`?:[0-9]+' "$GG_ROOT/$d" \
    | tr -d '`' | sort -u | while IFS= read -r ref; do
      f="${ref%:*}"; n="${ref##*:}"
      p=""
      [ -f "$GG_ROOT/$f" ] && p="$GG_ROOT/$f"
      [ -z "$p" ] && [ -f "$GG_ROOT/frontend/$f" ] && p="$GG_ROOT/frontend/$f"
      # 簡寫也要查。派工書寫 Order.cs:326 比寫完整路徑好讀，而 ④ 早就會解簡寫；
      # ⑤ 卻在解不出來時直接 continue——結果是它唯一該擋的那種引用剛好全被跳過，
      # 而且跳得無聲無息，看起來跟「查過都沒事」一模一樣。
      if [ -z "$p" ]; then
        hits="$(gg_resolve_ref "$f")"
        cnt="$(printf '%s' "$hits" | grep -c . || true)"
        [ "${cnt:-0}" -eq 1 ] && p="$GG_ROOT/$hits"
      fi
      seen
      if [ -z "$p" ]; then
        # 標明是新檔的就不算（跟 ④ 同一套豁免）
        grep -F -- "$f" "$GG_ROOT/$d" | grep -qE "$NEWMARK" && continue
        printf '  ✗ %s 引用 %s，但那個檔在這棵樹上找不到（或簡寫對到多個檔）\n' "$d" "$ref"
        echo "AUDIT_FAIL" >> "$GG_ROOT/.dispatch/.audit-flag"
        continue
      fi
      lines="$(wc -l < "$p")"
      [ "$n" -le "$lines" ] && continue
      printf '  ✗ %s 引用 %s，但那個檔只有 %s 行\n' "$d" "$ref" "$lines"
      echo "AUDIT_FAIL" >> "$GG_ROOT/.dispatch/.audit-flag"
    done
done
if [ -f "$GG_ROOT/.dispatch/.audit-flag" ]; then FAIL=1; CFAIL=1; rm -f "$GG_ROOT/.dispatch/.audit-flag"; fi
tally "行號引用都在範圍內" "派工書裡一個「檔案:行號」引用都沒抓到——抓取用的正規表示式可能失效了"

# ── 6. 每個生效包都要有啟動 prompt ──────────────────────────────
say "⑥ 每個生效包都有啟動 prompt"
missing="$(gg_prompts_missing)"
if [ -n "$missing" ]; then bad "PROMPTS.md 缺：$missing"; else ok "全部涵蓋"; fi

say "⑦ 兩棵樹的共用文件不得分岔"
# 用 worktree 不拆 repo 的整個理由就是「兩邊看同一份契約 YAML」（見 ADR 與 KB）。
# 一旦分岔，那個理由就沒了——而且分岔是安靜的：
# 第七波實際發生過，前端樹的 docs/00-decisions.md 少了 ADR-022~028，
# 於是 FE-14／FE-16 被要求實作它們在自己樹裡讀不到的 ADR。
#
# ★ 比對要忽略換行差異。CRLF/LF 不同不是分岔，
#   誤報久了真分岔會被當雜訊——這個專案已經在別處吃過這個虧。
GG_SIBLING=""
case "$GG_ROOT" in
  *-fe) GG_SIBLING="${GG_ROOT%-fe}" ;;
  *)    [ -d "${GG_ROOT}-fe" ] && GG_SIBLING="${GG_ROOT}-fe" ;;
esac
if [ -z "$GG_SIBLING" ] || [ ! -d "$GG_SIBLING" ]; then
  printf '  · 找不到另一棵 worktree，跳過
'
else
  shared_diverged=0
  # 閘門本身也要比。這一輪為了改閘門，我手動 cp 到另一棵樹四次；
  # 只要有一次忘了，兩棵樹的判斷規則就不一樣，而且沒有任何東西會說話。
  # ★ .claude/settings.json 刻意不在這張清單裡——它帶各樹自己的
  #   CLAUDE_PROJECT_DIR 後備路徑，那三行本來就該不同，不要去「同步」它。
  for f in docs/00-decisions.md docs/05-API契約.md            docs/api/openapi.admin.yaml docs/api/openapi.storefront.yaml \
           .dispatch/lib.sh .dispatch/shell-guard-lib.sh .dispatch/selftest.sh \
           .dispatch/audit-dispatch.sh .dispatch/gate-fingerprint.sh .dispatch/check-progress.py \
           .dispatch/PROMPTS.md .dispatch/reports/README.md \
           .claude/hooks/dispatch-guard.sh .claude/hooks/stop-gate.sh \
           .claude/hooks/session-brief.sh .claude/hooks/claim-package.sh \
           .codex/hooks/dispatch-guard.sh .codex/hooks/stop-gate.sh \
           .codex/hooks/session-brief.sh .codex/hooks/claim-package.sh .codex/hooks.json; do
    seen
    if [ ! -f "$GG_ROOT/$f" ] || [ ! -f "$GG_SIBLING/$f" ]; then
      bad "$f 只存在於一棵樹——另一棵樹的閘門或文件缺這一份"
      shared_diverged=1
      continue
    fi
    a="$(tr -d "\015" < "$GG_ROOT/$f" | md5sum | cut -d' ' -f1)"
    b="$(tr -d "\015" < "$GG_SIBLING/$f" | md5sum | cut -d' ' -f1)"
    [ "$a" = "$b" ] && continue
    case "$f" in
      docs/*) bad "$f 兩棵樹內容不同——契約／ADR 分岔，子代理會讀到不一樣的規則" ;;
      *)      bad "$f 兩棵樹內容不同——閘門分岔，兩邊的判斷規則會不一樣" ;;
    esac
    shared_diverged=1
  done
  tally "共用文件與閘門兩棵樹一致（忽略換行）" "一個檔都沒比到——另一棵樹的路徑可能不對"
fi


say "⑧ 每個生效包都留下自驗報告"
# 子代理提前結束累計 5 次，其中 3 次是在「你只有這一輪」寫進 PROMPTS.md 之後。
# 最危險的一次：BE-18 結束時手上有一條失敗的測試沒交代，而 exit code 是 0。
# 光加 prompt 規則沒用——所以改成機械判準：報告是檔案，寫不完就看得到。
# ★ 只在「這一包真的動過檔案」時才要求報告。
# 派工當下每包都還沒跑，這時候報缺報告是必然的誤報——
# 而必然的誤報會訓練所有人忽略這個檢查。有交付才查。
rep_missing=""
for pkg in $pkgs; do
  delivered="$(GG_PACKAGE="$pkg" gg_allow_list | while IFS= read -r pre; do
      [ -n "$pre" ] || continue
      git -C "$GG_ROOT" -c core.quotepath=false status --porcelain -- "$pre" 2>/dev/null
    done | grep -c . || true)"
  [ "${delivered:-0}" -gt 0 ] || continue
  f="$GG_ROOT/.dispatch/reports/${pkg}.md"
  if [ ! -f "$f" ]; then
    rep_missing="${rep_missing}${rep_missing:+、}${pkg}(有交付但無報告)"
    continue
  fi
  for h in "## 指令與輸出" "## 逐條自驗" "## 我發現但沒做的事"; do
    grep -qF "$h" "$f" || rep_missing="${rep_missing}${rep_missing:+、}${pkg}(缺 ${h})"
  done
done
if [ -n "$rep_missing" ]; then
  bad "自驗報告不完整：${rep_missing}（規格見 .dispatch/reports/README.md）"
else
  ok "有交付的包都有完整的自驗報告（沒動過檔案的包不要求）"
fi


say "⑨ 進度數字要自洽（總表逐節相加 = 合計 = 儀表板）"
# 邏輯在 .dispatch/check-progress.py（獨立檔，不要塞回這裡——
# 巢狀 heredoc 會把跳脫吃掉，這棵樹上已經發生五次）。
GG_PM=""
for cand in "$GG_ROOT/../GreyGray_PM" "$GG_ROOT/../../GreyGray_PM"; do
  [ -d "$cand" ] && { GG_PM="$cand"; break; }
done
if [ -z "$GG_PM" ]; then
  printf '  · 找不到 GreyGray_PM，跳過
'
elif [ ! -f "$GG_ROOT/.dispatch/check-progress.py" ]; then
  printf '  · 找不到 check-progress.py，跳過
'
else
  prog_out="$(python "$GG_ROOT/.dispatch/check-progress.py" "$GG_PM" 2>&1)"
  case "$prog_out" in
    OK*)   ok "${prog_out#OK }" ;;
    FAIL*) bad "${prog_out#FAIL }" ;;
    *)     printf '  · %s
' "$prog_out" ;;
  esac
fi

say "⑩ 閘門改過就要重跑自我測試"
# 為什麼要有這一項：前三次檢查閘門，每一次都還能再找到新的邏輯洞
# （3 個 → 8 個 → 1 個）。靠「我記得要再看一遍」不會收斂。
# selftest.sh 窮舉 125 項行為；這一項只確認「它跑過，而且是對現在這份閘門跑的」。
if [ ! -f "$GG_ROOT/.dispatch/selftest.sh" ] || [ ! -f "$GG_ROOT/.dispatch/gate-fingerprint.sh" ]; then
  printf '  · 找不到 selftest，跳過
'
else
  . "$GG_ROOT/.dispatch/gate-fingerprint.sh"
  fp_now="$(gg_gate_fingerprint "$GG_ROOT")"
  stamp="$GG_ROOT/.dispatch/.selftest-stamp"
  if [ ! -f "$stamp" ]; then
    bad "閘門自我測試從未跑過（沒有 .selftest-stamp）。跑 bash .dispatch/selftest.sh"
  else
    # 兩個 agent 都要對得上。舊版只比一行、不看是誰跑的，
    # 於是「只跑 claude」跟「兩個都跑」在這裡長得一模一樣——
    # 這一項的訊息從第一天就寫著「兩個 agent 都要」，但它沒在查。
    stale=""
    for a in claude codex; do
      line="$(grep "^${a} " "$stamp" 2>/dev/null | head -1)"
      if [ -z "$line" ]; then
        stale="${stale}${stale:+、}${a}(沒跑過)"
      elif [ "$(printf '%s' "$line" | cut -d' ' -f2)" != "$fp_now" ]; then
        stale="${stale}${stale:+、}${a}(對的是舊閘門)"
      fi
    done
    fp_old="$fp_now"; [ -n "$stale" ] && fp_old="stale"
    if [ "$fp_now" != "$fp_old" ]; then
      bad "閘門自我測試沒跟上：${stale}。跑 bash .dispatch/selftest.sh --agent <claude|codex>"
    else
      ok "兩個 agent 的自我測試都對得上現在這份閘門（$(awk '{printf "%s %s 項 ", $1, $3}' "$stamp")）"
    fi
  fi
fi


say ""
if [ "$FAIL" -eq 0 ]; then
  say "稽核通過。"
else
  say "稽核未通過——上面每一條 ✗ 都是派工書會讓子代理做不完或做錯的地方。"
fi
exit "$FAIL"
