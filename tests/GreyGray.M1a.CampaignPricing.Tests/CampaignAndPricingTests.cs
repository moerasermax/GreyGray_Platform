using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Campaign.Core;
using GreyGray.Modules.Campaign.Infra;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Modules.Pricing.Core;
using GreyGray.Modules.Pricing.Infra;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Platform.Outbox;
using GreyGray.Shared.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.CampaignPricing.Tests;

public sealed class CampaignAndPricingTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 28, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Campaign_transitions_and_events_stay_in_core()
    {
        var tenantId = new TenantId(Guid.Parse("00000000-0000-0000-0000-000000000088"));
        var draft = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            tenantId,
            ValidDraft(),
            Now);
        draft.IsSuccess.ShouldBeTrue();

        var offer = draft.Value.AddOffer(
            new CampaignOfferInput(
                SkuId.New(),
                Money.OfMajor(880, Currency.TWD),
                Money.OfMajor(700, Currency.TWD)),
            Now);
        offer.IsSuccess.ShouldBeTrue();

        var published = draft.Value.Publish(Now);
        published.IsSuccess.ShouldBeTrue();
        published.Value.TenantId.ShouldBe(tenantId);
        published.Value.CampaignId.ShouldBe(draft.Value.Id);
        published.Value.OccurredAt.ShouldBe(Now);
        draft.Value.Status.ShouldBe(CampaignStatus.Open);

        draft.Value.UpdateDraft(ValidDraft() with { Title = "不可修改" }, Now)
            .Error.Code.ShouldBe("campaign.cannot-edit-after-publish");

        var closed = draft.Value.Close(Now.AddHours(1));
        closed.IsSuccess.ShouldBeTrue();
        closed.Value.CampaignId.ShouldBe(draft.Value.Id);
        draft.Value.Status.ShouldBe(CampaignStatus.Closed);

        draft.Value.Settle(true, Now.AddHours(2)).Error.Code
            .ShouldBe("campaign.invalid-transition");

        var cancelled = draft.Value.Cancel("航班取消", Now.AddHours(2));
        cancelled.IsSuccess.ShouldBeTrue();
        cancelled.Value.Reason.ShouldBe("航班取消");
        draft.Value.Status.ShouldBe(CampaignStatus.Cancelled);
    }

    [Fact]
    public void Campaign_requires_an_active_offer_before_publish()
    {
        var draft = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft(),
            Now).Value;

        draft.Publish(Now).Error.Code.ShouldBe("campaign.offer-required");
    }

    [Fact]
    public void Campaign_settle_requires_returned_state_and_all_orders_shipped()
    {
        var campaign = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft(),
            Now).Value;
        typeof(CampaignAggregate).GetProperty(nameof(CampaignAggregate.Status))!
            .SetValue(campaign, CampaignStatus.Returned);

        campaign.Settle(false, Now).Error.Code
            .ShouldBe("campaign.orders-not-all-shipped");

        var settled = campaign.Settle(true, Now);
        settled.IsSuccess.ShouldBeTrue();
        settled.Value.CampaignId.ShouldBe(campaign.Id);
        settled.Value.OccurredAt.ShouldBe(Now);
        campaign.Status.ShouldBe(CampaignStatus.Settled);
    }

    [Fact]
    public void Offer_with_orders_cannot_be_removed_and_draft_offer_can_be_reactivated()
    {
        var campaign = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft(),
            Now).Value;
        var skuId = SkuId.New();
        var offer = campaign.AddOffer(
            new CampaignOfferInput(skuId, Money.OfMajor(800, Currency.TWD), null),
            Now).Value;

        campaign.RemoveOffer(offer.Id, true, Now).Error.Code
            .ShouldBe("campaign.offer-has-orders");
        offer.IsActive.ShouldBeTrue();

        campaign.RemoveOffer(offer.Id, false, Now).IsSuccess.ShouldBeTrue();
        offer.IsActive.ShouldBeFalse();

        var reactivated = campaign.AddOffer(
            new CampaignOfferInput(skuId, Money.OfMajor(850, Currency.TWD), null),
            Now.AddMinutes(1));
        reactivated.Value.Id.ShouldBe(offer.Id);
        reactivated.Value.SellingPrice.ShouldBe(Money.OfMajor(850, Currency.TWD));
        reactivated.Value.IsActive.ShouldBeTrue();
    }

    [Fact(DisplayName = "ADR-027：漲價詢問逾時每團可設，沒填就是 null，讓呼叫端自己套用技術預設值")]
    public void Price_inquiry_timeout_is_optional_and_flows_into_summary()
    {
        var withoutOverride = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft() with { PriceInquiryTimeoutMinutes = null },
            Now).Value;
        withoutOverride.ToSummary().PriceInquiryTimeout.ShouldBeNull();

        var withOverride = CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft() with { PriceInquiryTimeoutMinutes = 90 },
            Now).Value;
        withOverride.ToSummary().PriceInquiryTimeout.ShouldBe(TimeSpan.FromMinutes(90));

        CampaignAggregate.CreateDraft(
            CampaignId.New(),
            TenantId.Default,
            ValidDraft() with { PriceInquiryTimeoutMinutes = 0 },
            Now).Error.Code.ShouldBe("campaign.invalid-price-inquiry-timeout");
    }

    [Theory]
    [InlineData(DeliveryMethod.ConvenienceStore, 6000)]
    [InlineData(DeliveryMethod.HomeDelivery, 12000)]
    [InlineData(DeliveryMethod.SelfPickup, 0)]
    public async Task M1a_quotes_flat_delivery_fee_and_complete_weight_snapshot(
        DeliveryMethod deliveryMethod,
        long expectedMinor)
    {
        var service = CreatePricingService(out _, out _);
        var request = new QuoteRequest(
            deliveryMethod,
            [
                new QuoteLineRequest(
                    SkuId.New(),
                    2,
                    500,
                    new Dimensions(30, 20, 10)),
            ],
            null,
            GreyGray.Modules.Identity.Contracts.MemberTier.Standard);

        var result = await service.QuoteAsync(request, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShippingFee.ShouldBe(new Money(expectedMinor, Currency.TWD));
        result.Value.ActualWeightGram.ShouldBe(1000);
        result.Value.VolumetricWeightGram.ShouldBe(2000);
        result.Value.BillableWeightGram.ShouldBe(2000);
        result.Value.AppliedRuleSetId.ShouldBe(FlatRatePricingService.M1aRuleSetId);
        result.Value.AppliedRuleId.ShouldNotBeNull();
        result.Value.AppliedStrategy.ShouldBe(ShippingStrategyKind.Flat);
        result.Value.Explain.ShouldNotBeEmpty();
        result.Value.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task Freeze_is_immutable_idempotent_and_returns_the_complete_first_snapshot()
    {
        var service = CreatePricingService(out var store, out var unitOfWork);
        var quoted = await service.QuoteAsync(
            new QuoteRequest(
                DeliveryMethod.HomeDelivery,
                [new QuoteLineRequest(SkuId.New(), 1, 900, new Dimensions(10, 10, 10))],
                null,
                GreyGray.Modules.Identity.Contracts.MemberTier.Standard),
            TestContext.Current.CancellationToken);
        var snapshot = quoted.Value;

        (await service.FreezeAsync(snapshot, TestContext.Current.CancellationToken))
            .Value.ShouldBe(snapshot.Id);
        (await service.FreezeAsync(
            snapshot with { Explain = snapshot.Explain.ToArray() },
            TestContext.Current.CancellationToken)).Value.ShouldBe(snapshot.Id);

        var retrieved = await service.GetSnapshotAsync(
            snapshot.Id,
            TestContext.Current.CancellationToken);
        retrieved.Value.ShippingFee.ShouldBe(snapshot.ShippingFee);
        retrieved.Value.Explain.ShouldBe(snapshot.Explain);
        retrieved.Value.AppliedRuleSetId.ShouldBe(snapshot.AppliedRuleSetId);
        store.Count.ShouldBe(1);
        unitOfWork.SaveCount.ShouldBe(1);

        var changed = await service.FreezeAsync(
            snapshot with { ShippingFee = Money.Zero(Currency.TWD) },
            TestContext.Current.CancellationToken);
        changed.Error.Code.ShouldBe("pricing.invalid-snapshot");
    }

    [Fact]
    public void Ef_models_keep_business_and_outbox_in_the_same_context()
    {
        using var campaign = new CampaignDbContext(
            new DbContextOptionsBuilder<CampaignDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);
        using var pricing = new PricingDbContext(
            new DbContextOptionsBuilder<PricingDbContext>()
                .UseNpgsql("Host=localhost;Database=greygray;Username=greygray;Password=greygray")
                .Options);

        campaign.Model.FindEntityType(typeof(CampaignAggregate))!
            .GetSchema().ShouldBe("campaign");
        campaign.Model.FindEntityType(typeof(OutboxMessage))!
            .GetSchema().ShouldBe("platform");
        pricing.Model.FindEntityType(typeof(PricingSnapshotEntity))!
            .GetSchema().ShouldBe("pricing");
        pricing.Model.FindEntityType(typeof(OutboxMessage))!
            .GetSchema().ShouldBe("platform");
    }

    [Fact]
    public void Infra_exports_only_each_module_registration_and_registration_is_lazy()
    {
        typeof(CampaignModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(CampaignModuleRegistration)], ignoreOrder: true);
        typeof(PricingModuleRegistration).Assembly.GetExportedTypes()
            .ShouldBe([typeof(PricingModuleRegistration)], ignoreOrder: true);

        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddCampaignModule(configuration);
        services.AddPricingModule(configuration);
        using var provider = services.BuildServiceProvider();
        provider.ShouldNotBeNull();
    }

    private static CampaignDraftInput ValidDraft() =>
        new(
            "首爾秋季連線",
            "首爾",
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 8),
            Now.AddDays(20),
            null,
            "採購期間限定商品",
            "https://example.invalid/campaign.jpg");

    private static FlatRatePricingService CreatePricingService(
        out MemoryPricingSnapshotStore store,
        out CountingUnitOfWork unitOfWork)
    {
        store = new MemoryPricingSnapshotStore();
        unitOfWork = new CountingUnitOfWork();
        return new FlatRatePricingService(store, unitOfWork, new FixedClock(Now));
    }
}

internal sealed class MemoryPricingSnapshotStore : IPricingSnapshotStore
{
    private readonly Dictionary<PricingSnapshotId, PricingSnapshot> _snapshots = [];

    public int Count => _snapshots.Count;

    public Task<PricingSnapshot?> GetAsync(
        PricingSnapshotId id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _snapshots.TryGetValue(id, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public void Add(PricingSnapshot snapshot) => _snapshots.Add(snapshot.Id, snapshot);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow => utcNow;

    public DateOnly TodayInTaipei => DateOnly.FromDateTime(utcNow.AddHours(8).DateTime);
}
