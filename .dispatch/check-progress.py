"""進度數字自洽檢查：逐節相加 = 合計 = 儀表板，且基準 commit 真的存在。

為什麼要有這支：C 階段的數字曾經是「32/36」，而進度總表上怎麼數都湊不出來——
因為那個數字從來不是算出來的，是手打進 dashboard.html 的；而同一個事實在
進度總表的列裡又有一份，中間沒有任何連結。分母還會自己長大（前端 8→11→14→17 包），
加包的人改列表、不改摘要。錯了不會有任何東西壞掉，連假綠燈都算不上。

同一族的第二個洞：「基準 commit」也是手打的，而且已經錯過一次——
第八波派工的 commit 被標成「第六波八包已提交」。

★ 這支**只輸出一行**：OK / SKIP <理由> / FAIL <說明>。
   第一版印了 OK 之後又印 FAIL，而呼叫端用 `case OK*)` 比對——
   等於一個會在失敗時安靜放行的檢查。不要再讓它印超過一行。
"""
import io
import re
import subprocess
import sys


def verdict(line):
    io.open(1, "w", encoding="utf-8", closefd=False).write(line + "\n")
    raise SystemExit


pm = sys.argv[1]

try:
    table = io.open(pm + "/00-進度總表.md", encoding="utf-8", newline="").read()
except OSError:
    verdict("SKIP 讀不到 00-進度總表.md")

# ── ① C 段開頭那張「怎麼算的」表：| 小節 | 項目數 | 完成數 | ──────
row_re = re.compile(
    r"^>?\s*\|\s*(?:\*\*)?\s*(C-\d[^|]*?|合計)\s*(?:\*\*)?\s*\|"
    r"[^|]*?(\d+)[^|]*\|"
    r"[^|]*?(\d+)[^|]*\|"
)
rows, total = [], None
for line in table.split("\n"):
    m = row_re.match(line)
    if not m:
        continue
    items, done = int(m.group(2)), int(m.group(3))
    if "合計" in m.group(1):
        total = (items, done)
    else:
        rows.append((items, done))

if not rows or total is None:
    verdict("SKIP 進度總表沒有可解析的計數表")

sum_items = sum(r[0] for r in rows)
sum_done = sum(r[1] for r in rows)
if (sum_items, sum_done) != total:
    verdict("FAIL 進度總表的合計（%d 項 %d 完成）與逐節相加（%d 項 %d 完成）對不上"
            % (total[0], total[1], sum_items, sum_done))

# ── ② 儀表板要與總表一致 ────────────────────────────────────────
try:
    dash = io.open(pm + "/web/dashboard.html", encoding="utf-8", newline="").read()
except OSError:
    verdict("SKIP 讀不到 dashboard.html")

m = re.search(r"k:'C'[^}]*?done:(\d+),total:(\d+)", dash)
if not m:
    verdict("SKIP 儀表板讀不到 C 的數字")

d_done, d_total = int(m.group(1)), int(m.group(2))
if (d_done, d_total) != (total[1], total[0]):
    verdict("FAIL 儀表板 C 是 %d/%d，進度總表是 %d/%d——同一個事實兩份表示對不上"
            % (d_done, d_total, total[1], total[0]))

# ── ③ 基準 commit 要真的存在，而且在 HEAD 的歷史上 ──────────────
def sha_problem(repo, sha):
    def run(*args):
        return subprocess.run(args, cwd=repo, capture_output=True, text=True)
    if run("git", "cat-file", "-e", sha + "^{commit}").returncode != 0:
        return "不存在"
    if run("git", "merge-base", "--is-ancestor", sha, "HEAD").returncode != 0:
        return "不在 HEAD 的歷史上"
    return None


m = re.search(r"\*\*基準\*\*：後端 `([0-9a-f]{7,40})`.*?前端 `([0-9a-f]{7,40})`", table)
if m:
    root = pm.rsplit("/", 1)[0]
    problems = []
    for sha, repo, label in ((m.group(1), root + "/GreyGray_Platform", "後端"),
                             (m.group(2), root + "/GreyGray_Platform-fe", "前端")):
        state = sha_problem(repo, sha)
        if state:
            problems.append("%s 基準 %s %s" % (label, sha, state))
    if problems:
        verdict("FAIL 進度總表的基準 commit 有問題：" + "；".join(problems))

verdict("OK 逐節相加 = 合計 = 儀表板（%d/%d），基準 commit 也對得上" % (total[1], total[0]))
