using GreyGray.Modules.Ordering.Infra;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CheckoutOrdering.Tests;

public sealed class PaymentDeadlineConfigurationTests
{
    [Theory(DisplayName = "BE-64 D9：繳費期限與非卡寬限設定為 0 或負數時，模組啟動立即失敗")]
    [InlineData("Ordering:PaymentDueHours", "0")]
    [InlineData("Ordering:PaymentDueHours", "-1")]
    [InlineData("Ordering:NonCardPaymentGraceDays", "0")]
    [InlineData("Ordering:NonCardPaymentGraceDays", "-1")]
    public void Non_positive_deadline_configuration_is_rejected(string key, string value)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [key] = value,
            })
            .Build();

        var exception = Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddOrderingModule(configuration));

        exception.Message.ShouldContain(key);
    }
}
