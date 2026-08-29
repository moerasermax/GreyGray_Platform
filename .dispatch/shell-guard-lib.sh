#!/usr/bin/env bash
# 會毀掉別人未提交工作的 shell 指令，一律擋下。
#
# 為什麼需要這一層：同一棵 worktree 裡有多個 agent 平行工作，
# 每個人的交付在被整合驗收之前都還沒 commit。
# 下面這些指令不分青紅皂白地動整個工作區，等於把別人的交付一起處理掉。
#
# 這不是假設性風險，是實際發生過的：
# FE-10 的 agent 為了「取得乾淨的驗證基準」跑了 git stash，
# 把 FE-9 已完成但還沒提交的交付、以及整合者正在改的文件一起掃進 stash。
# 後果是整合者當下的 commit 只記錄到三個檔案裡的一個，而訊息說了三個。
# git stash 的 stack 還是跨 worktree 共用的，別棵樹的 session 也會被波及。

# 回傳 0 = 這是危險指令（要擋），並在 stdout 印出理由
gg_dangerous_shell_reason() {
  local c="$1"
  # 去掉多餘空白，方便比對
  c="$(printf '%s' "$c" | tr '\n' ' ' | sed 's/  */ /g')"

  case "$c" in
    *"git stash"*)
      printf '%s' "git stash 會把工作區裡**所有人**未提交的變更一起收走，包含別包已完成但還沒被整合驗收的交付；而且 stash stack 是跨 worktree 共用的，另一棵樹的 session 也會被波及。要取得乾淨的驗證基準，請改用不會動到工作區的方式（例如 git diff、git stash create 只建物件不動工作區），或直接停下來回報。"
      return 0 ;;
    *"git reset --hard"*|*"git reset"*"--hard"*)
      printf '%s' "git reset --hard 會丟掉工作區裡所有人未提交的變更，不只你的。要撤銷自己的檔案請指名檔案：git checkout -- <你這一包的檔案>。"
      return 0 ;;
    *"git checkout -- ."*|*"git checkout ."|*"git restore ."*|*"git restore -- ."*)
      printf '%s' "整個工作區的還原會蓋掉別包未提交的交付。請指名到檔案，只還原你這一包 allow 清單裡的路徑。"
      return 0 ;;
    *"git clean"*)
      printf '%s' "git clean 會刪掉工作區裡所有人的未追蹤檔案，包含別包剛建立、還沒提交的新檔。要清自己的檔案請逐一指名。"
      return 0 ;;
    *"git commit"*)
      printf '%s' "提交是整合者的工作，不是實作者的。你的交付留在工作區，自驗輸出貼出來就好——整合驗收會逐包核對所有權表之後才提交。自己 commit 會把別包同時在做的變更一起帶進去。"
      return 0 ;;
  esac
  return 1
}
