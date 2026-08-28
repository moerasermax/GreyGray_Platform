/*
 * 骨架頁。**會被前端工作包 FE-6 整個換掉**（後台首頁＝營運儀表板）。
 *
 * 首頁上最重要的一塊是「負債 vs 現金」——`isBreached = true` 代表
 * 正在用還沒交貨的錢過日子，那是這門生意最典型的崩壞前兆，要放在看得到的地方。
 */
export default function AdminHome() {
  return (
    <main className="mx-auto max-w-[1400px] p-6">
      <h1 className="text-xl font-semibold text-fg">GreyGray 後台</h1>
      <p className="mt-1 text-sm text-fg-muted">
        骨架已就緒。這頁是佔位用的，會被 FE-6 換掉。
      </p>

      <div className="mt-6 rounded-card border border-border-soft bg-surface p-5 shadow-card">
        <h2 className="text-sm font-medium text-fg-muted">Token 檢查</h2>
        <table className="mt-3 w-full text-sm">
          <thead>
            <tr className="border-b border-border-soft text-left text-fg-muted">
              <th className="py-2 font-medium">科目</th>
              <th className="py-2 font-medium">方向</th>
              <th className="py-2 text-right font-medium" data-numeric>
                金額
              </th>
            </tr>
          </thead>
          <tbody>
            <tr className="border-b border-border-soft">
              <td className="py-2">預收貨款</td>
              <td className="py-2" data-direction="Credit">
                貸
              </td>
              <td className="py-2" data-numeric>
                280,000
              </td>
            </tr>
            <tr>
              <td className="py-2">綠界在途</td>
              <td className="py-2" data-direction="Debit">
                借
              </td>
              <td className="py-2" data-numeric>
                300,000
              </td>
            </tr>
          </tbody>
        </table>
        <p className="mt-3 text-xs text-fg-muted">
          兩列的數字位數應該上下對齊（tabular-nums）；借貸除了顏色還要有文字。
        </p>
      </div>
    </main>
  );
}
