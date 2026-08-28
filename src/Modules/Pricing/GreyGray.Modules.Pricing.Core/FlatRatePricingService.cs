using GreyGray.Modules.Pricing.Contracts;
using GreyGray.Platform.Abstractions.Messaging;
using GreyGray.Shared.Kernel;

namespace GreyGray.Modules.Pricing.Core;

internal interface IPricingSnapshotStore
{
    Task<PricingSnapshot?> GetAsync(
        PricingSnapshotId id,
        CancellationToken cancellationToken);

    void Add(PricingSnapshot snapshot);
}

internal sealed class FlatRatePricingService(
    IPricingSnapshotStore snapshots,
    IUnitOfWork unitOfWork,
    IClock clock) : IPricingQuotation
{
    internal static readonly FeeRuleSetId M1aRuleSetId =
        new(new Guid("0198c3d4-e5f6-7018-9abc-000000000001"));

    private static readonly IReadOnlyDictionary<DeliveryMethod, FeeRuleId> RuleIds =
        new Dictionary<DeliveryMethod, FeeRuleId>
        {
            [DeliveryMethod.ConvenienceStore] =
                new(new Guid("0198c3d4-e5f6-7018-9abc-000000000061")),
            [DeliveryMethod.HomeDelivery] =
                new(new Guid("0198c3d4-e5f6-7018-9abc-000000000120")),
            [DeliveryMethod.SelfPickup] =
                new(new Guid("0198c3d4-e5f6-7018-9abc-000000000000")),
        };

    public Task<Result<PricingSnapshot>> QuoteAsync(
        QuoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!RuleIds.TryGetValue(request.DeliveryMethod, out var ruleId))
        {
            return Task.FromResult(Result<PricingSnapshot>.Failure(
                "pricing.unsupported-delivery-method",
                "不支援指定的配送方式。"));
        }

        var weights = CalculateWeights(request.Lines);
        if (weights.IsFailure)
        {
            return Task.FromResult(Result<PricingSnapshot>.Failure(weights.Error));
        }

        var shippingFee = request.DeliveryMethod switch
        {
            DeliveryMethod.ConvenienceStore => Money.OfMajor(60, Currency.TWD),
            DeliveryMethod.HomeDelivery => Money.OfMajor(120, Currency.TWD),
            DeliveryMethod.SelfPickup => Money.Zero(Currency.TWD),
            _ => throw new InvalidOperationException("出現未處理的配送方式。"),
        };

        var explanation = request.DeliveryMethod switch
        {
            DeliveryMethod.ConvenienceStore => "M1a 固定一口價：超商取貨 NT$60。",
            DeliveryMethod.HomeDelivery => "M1a 固定一口價：宅配 NT$120。",
            DeliveryMethod.SelfPickup => "M1a 固定一口價：面交／自取免運費。",
            _ => throw new InvalidOperationException("出現未處理的配送方式。"),
        };

        var (actual, volumetric) = weights.Value;
        var snapshot = new PricingSnapshot(
            PricingSnapshotId.New(),
            request.DeliveryMethod,
            actual,
            volumetric,
            Math.Max(actual, volumetric),
            shippingFee,
            M1aRuleSetId,
            ruleId,
            ShippingStrategyKind.Flat,
            [explanation],
            clock.UtcNow);
        return Task.FromResult(Result<PricingSnapshot>.Success(snapshot));
    }

    public async Task<Result<PricingSnapshotId>> FreezeAsync(
        PricingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var validation = ValidateSnapshot(snapshot);
        if (validation.IsFailure)
        {
            return Result<PricingSnapshotId>.Failure(validation.Error);
        }

        var existing = await snapshots.GetAsync(snapshot.Id, cancellationToken);
        if (existing is not null)
        {
            return SnapshotsEqual(existing, snapshot)
                ? snapshot.Id
                : Result<PricingSnapshotId>.Failure(
                    "pricing.snapshot-id-reused",
                    "報價快照識別碼已被不同內容使用。");
        }

        snapshots.Add(snapshot);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return snapshot.Id;
    }

    public async Task<Result<PricingSnapshot>> GetSnapshotAsync(
        PricingSnapshotId id,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.GetAsync(id, cancellationToken);
        return snapshot is null
            ? Result<PricingSnapshot>.Failure("pricing.snapshot-not-found", "找不到指定的報價快照。")
            : snapshot;
    }

    private static Result<(int Actual, int Volumetric)> CalculateWeights(
        IReadOnlyList<QuoteLineRequest> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        long actual = 0;
        long volumetric = 0;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                return Result<(int, int)>.Failure(
                    "pricing.invalid-quantity",
                    "詢價商品數量必須大於零。");
            }

            if (line.WeightGram < 0
                || line.Size.LengthCm < 0
                || line.Size.WidthCm < 0
                || line.Size.HeightCm < 0)
            {
                return Result<(int, int)>.Failure(
                    "pricing.invalid-dimensions",
                    "商品重量與尺寸不可為負數。");
            }

            try
            {
                actual = checked(actual + checked((long)line.WeightGram * line.Quantity));
                var oneVolumetric = line.Size.VolumetricWeightGram(6000);
                volumetric = checked(volumetric + checked((long)oneVolumetric * line.Quantity));
            }
            catch (OverflowException)
            {
                return Result<(int, int)>.Failure(
                    "pricing.weight-overflow",
                    "詢價商品的總重量超出可處理範圍。");
            }
        }

        if (actual > int.MaxValue || volumetric > int.MaxValue)
        {
            return Result<(int, int)>.Failure(
                "pricing.weight-overflow",
                "詢價商品的總重量超出可處理範圍。");
        }

        return ((int)actual, (int)volumetric);
    }

    private static Result ValidateSnapshot(PricingSnapshot snapshot)
    {
        if (snapshot.Id.Value == Guid.Empty)
        {
            return Result.Failure("pricing.invalid-snapshot", "報價快照識別碼不可為空。");
        }

        if (snapshot.AppliedRuleSetId != M1aRuleSetId
            || snapshot.AppliedStrategy != ShippingStrategyKind.Flat
            || !RuleIds.TryGetValue(snapshot.DeliveryMethod, out var ruleId)
            || snapshot.AppliedRuleId != ruleId)
        {
            return Result.Failure(
                "pricing.invalid-snapshot",
                "報價快照不是目前 M1a 一口價規則產生的完整結果。");
        }

        if (snapshot.ActualWeightGram < 0
            || snapshot.VolumetricWeightGram < 0
            || snapshot.BillableWeightGram != Math.Max(
                snapshot.ActualWeightGram,
                snapshot.VolumetricWeightGram)
            || snapshot.Explain.Count == 0)
        {
            return Result.Failure("pricing.invalid-snapshot", "報價快照的重量或說明不完整。");
        }

        var expectedFee = snapshot.DeliveryMethod switch
        {
            DeliveryMethod.ConvenienceStore => Money.OfMajor(60, Currency.TWD),
            DeliveryMethod.HomeDelivery => Money.OfMajor(120, Currency.TWD),
            DeliveryMethod.SelfPickup => Money.Zero(Currency.TWD),
            _ => Money.Zero(Currency.TWD),
        };
        return snapshot.ShippingFee == expectedFee
            ? Result.Success()
            : Result.Failure("pricing.invalid-snapshot", "報價快照的運費與套用規則不一致。");
    }

    private static bool SnapshotsEqual(PricingSnapshot left, PricingSnapshot right) =>
        left.Id == right.Id
        && left.DeliveryMethod == right.DeliveryMethod
        && left.ActualWeightGram == right.ActualWeightGram
        && left.VolumetricWeightGram == right.VolumetricWeightGram
        && left.BillableWeightGram == right.BillableWeightGram
        && left.ShippingFee == right.ShippingFee
        && left.AppliedRuleSetId == right.AppliedRuleSetId
        && left.AppliedRuleId == right.AppliedRuleId
        && left.AppliedStrategy == right.AppliedStrategy
        && left.Explain.SequenceEqual(right.Explain, StringComparer.Ordinal)
        && left.CreatedAt == right.CreatedAt;
}
