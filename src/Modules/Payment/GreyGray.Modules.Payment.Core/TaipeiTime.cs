namespace GreyGray.Modules.Payment.Core;

/// <summary>
/// 台北時區。<b>不要直接寫 <c>TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei")</c>。</b>
/// </summary>
/// <remarks>
/// <para>
/// <c>Directory.Build.props</c> 把 <c>InvariantGlobalization</c> 設成 true，那會關掉 ICU；
/// <b>Windows 上沒有 ICU 就查不到 IANA 的 "Asia/Taipei"</b>，只認 Windows 自己的
/// "Taipei Standard Time"，而 Linux 相反（讀 <c>/usr/share/zoneinfo</c>，只有 IANA 那個名字）。
/// 兩個都試才在兩種部署上都成立。
/// </para>
/// <para>
/// 為什麼要有這個檔：BE-40 寫下第一條真的呼叫
/// <c>EcpayGateway.CreateCheckoutFields</c> 的測試時，它在這一行就丟
/// <c>TimeZoneNotFoundException</c>——也就是說<b>付款發動在 Windows 上從來沒有成功過</b>，
/// 而在此之前沒有任何測試走到這條路（所有測試都用 <c>StubGateway</c>）。
/// 2026-09-02 在 GreyGray.Tools.EcpaySimulator（同樣的 <c>InvariantGlobalization</c> 設定）
/// 的真實行程上重現過一次：<c>POST /Cashier/AioCheckOut/V5/decide</c> 回 500，
/// 例外訊息一字不差。
/// </para>
/// <para>
/// 台北自 1980 年起沒有日光節約時間，兩個 ID 都是固定 UTC+8——這個改法不改變任何換算結果。
/// </para>
/// <para>
/// <b>同型問題還在 <c>GreyGray.Platform.Time.SystemClock</c>（<c>TodayInTaipei</c>）。</b>
/// 那個檔不在 BE-40 的授權路徑內，沒有動；正確的長期做法是把這段搬到
/// <c>Shared.Kernel</c> 或 <c>Platform</c>，讓全專案只有一份。
/// </para>
/// </remarks>
internal static class TaipeiTime
{
    private static readonly string[] CandidateIds = ["Asia/Taipei", "Taipei Standard Time"];

    /// <summary>台北時區（UTC+8，無日光節約）。</summary>
    internal static TimeZoneInfo Zone { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in CandidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // 換下一個候選名字。兩個都查不到才是真的有問題。
            }
        }

        throw new InvalidOperationException(
            $"這台機器查不到台北時區（試過 {string.Join("、", CandidateIds)}）。" +
            "截團、逾期未付、綠界的 MerchantTradeDate 全都靠它，缺了不能繼續。");
    }
}
