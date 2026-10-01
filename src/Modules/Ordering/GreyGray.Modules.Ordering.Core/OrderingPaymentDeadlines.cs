namespace GreyGray.Modules.Ordering.Core;

/// <summary>訂單付款期限與非信用卡逾期寬限。</summary>
internal sealed class OrderingPaymentDeadlines
{
    public static OrderingPaymentDeadlines Default { get; } = new(
        TimeSpan.FromHours(24),
        TimeSpan.FromDays(2));

    public OrderingPaymentDeadlines(TimeSpan paymentDue, TimeSpan nonCardGrace)
    {
        if (paymentDue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentDue), "付款期限必須大於 0。");
        }

        if (nonCardGrace <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(nonCardGrace), "非信用卡寬限必須大於 0。");
        }

        PaymentDue = paymentDue;
        NonCardGrace = nonCardGrace;
    }

    public TimeSpan PaymentDue { get; }

    public TimeSpan NonCardGrace { get; }
}
