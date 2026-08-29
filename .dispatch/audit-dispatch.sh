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
say()  { printf '%s\n' "$1"; }
bad()  { printf '  ✗ %s\n' "$1"; FAIL=1; }
ok()   { printf '  ✓ %s\n' "$1"; }

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

# ── 1. allow 路徑要真的存在（新檔除外）──────────────────────────
say "① allow 路徑存在性"
for pkg in $pkgs; do
  while IFS= read -r prefix; do
    [ -n "$prefix" ] || continue
    case "$prefix" in
      db/migrations/*) continue ;;   # 編號前綴，第 2 條另外查
    esac
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

# ── 2. migration 編號沒被用過 ───────────────────────────────────
say "② migration 編號未被佔用"
for pkg in $pkgs; do
  while IFS= read -r prefix; do
    case "$prefix" in
      db/migrations/*)
        num="${prefix#db/migrations/}"
        hit="$(ls "$GG_ROOT/db/migrations/" 2>/dev/null | grep -c "^${num}" || true)"
        if [ "${hit:-0}" -gt 0 ]; then
          bad "$pkg 拿到的 migration 編號 $num 已經有 $hit 個檔了（一號一檔）"
        else
          ok "$pkg → $num 未被佔用"
        fi ;;
    esac
  done <<EOF
$(GG_PACKAGE="$pkg" gg_allow_list)
EOF
done

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
              bad "$a 與 $b 都擁有 $pa"
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
[ "$overlap" -eq 0 ] && ok "沒有重疊（tests/ 是已知的共用，由總驗收逐檔看 diff）"


say "④ 派工書引用的檔案存在"
docs="$(gg_active_docs | sort -u)"
for d in $docs; do
  [ -f "$GG_ROOT/$d" ] || { bad "派工書本身不存在：$d"; continue; }
  # 反引號裡、看起來像原始碼路徑的 token
  grep -oE '`[A-Za-z0-9_./-]+\.(cs|ts|tsx|sql|ps1|yaml|yml|json|sh)`' "$GG_ROOT/$d" \
    | tr -d '`' | sort -u | while IFS= read -r f; do
      [ -n "$f" ] || continue
      case "$f" in */*) ;; *) continue ;; esac        # 只查有路徑的
      [ -e "$GG_ROOT/$f" ] && continue
      # 那一行有沒有標明是新檔
      if grep -F -- "$f" "$GG_ROOT/$d" | grep -qE "$NEWMARK"; then continue; fi
      # 可能是相對於 frontend/ 的路徑
      [ -e "$GG_ROOT/frontend/$f" ] && continue
      # 簡寫路徑：只要能「唯一」對到一個真檔就算數。
      # 派工書寫 Ordering.Infra/ModuleRegistration.cs 比寫完整路徑好讀，
      # 但前提是它指得明確——對到兩個以上就是模糊，agent 要猜，那就是問題。
      hits="$(cd "$GG_ROOT" && git ls-files | grep -F -- "$f" | head -5)"
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
[ -f "$GG_ROOT/.dispatch/.audit-flag" ] && { FAIL=1; rm -f "$GG_ROOT/.dispatch/.audit-flag"; } || ok "沒有引用不存在的檔"

# ── 5. `檔案:行號` 的引用，行數要夠 ─────────────────────────────
say "⑤ 行號引用不超出檔案長度"
for d in $docs; do
  [ -f "$GG_ROOT/$d" ] || continue
  grep -oE '`?[A-Za-z0-9_./-]+\.(cs|ts|tsx|sql|ps1|yaml)`?:[0-9]+' "$GG_ROOT/$d" \
    | tr -d '`' | sort -u | while IFS= read -r ref; do
      f="${ref%:*}"; n="${ref##*:}"
      p=""
      [ -f "$GG_ROOT/$f" ] && p="$GG_ROOT/$f"
      [ -z "$p" ] && [ -f "$GG_ROOT/frontend/$f" ] && p="$GG_ROOT/frontend/$f"
      [ -n "$p" ] || continue
      lines="$(wc -l < "$p")"
      [ "$n" -le "$lines" ] && continue
      printf '  ✗ %s 引用 %s，但那個檔只有 %s 行\n' "$d" "$ref" "$lines"
      echo "AUDIT_FAIL" >> "$GG_ROOT/.dispatch/.audit-flag"
    done
done
[ -f "$GG_ROOT/.dispatch/.audit-flag" ] && { FAIL=1; rm -f "$GG_ROOT/.dispatch/.audit-flag"; } || ok "行號引用都在範圍內"

# ── 6. 每個生效包都要有啟動 prompt ──────────────────────────────
say "⑥ 每個生效包都有啟動 prompt"
missing="$(gg_prompts_missing)"
if [ -n "$missing" ]; then bad "PROMPTS.md 缺：$missing"; else ok "全部涵蓋"; fi

say ""
if [ "$FAIL" -eq 0 ]; then
  say "稽核通過。"
else
  say "稽核未通過——上面每一條 ✗ 都是派工書會讓子代理做不完或做錯的地方。"
fi
exit "$FAIL"
