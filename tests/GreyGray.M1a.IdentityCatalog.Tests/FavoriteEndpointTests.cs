using GreyGray.Api.Storefront;
using GreyGray.Modules.Campaign.Contracts;
using GreyGray.Modules.Catalog.Contracts;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Platform.Abstractions.Idempotency;
using GreyGray.Platform.Abstractions.Sessions;
using GreyGray.Shared.Kernel;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace GreyGray.M1a.IdentityCatalog.Tests;

public sealed class FavoriteEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "最愛三端點：匿名一律 401，且不碰 Catalog 或冪等儲存")]
    public async Task Anonymous_favorite_endpoints_are_unauthorized_without_side_effects()
    {
        var sessions = new MemorySessionStore();
        var favorites = new EndpointFavorites();
        var idempotency = new MemoryIdempotencyStore();
        var productId = ProductId.New().ToString();
        var cancellationToken = TestContext.Current.CancellationToken;

        Status(await M1aEndpoints.GetFavoritesAsync(
            Context(null, null), sessions, favorites, new FakeCampaignStorefront(), null, null,
            cancellationToken)).ShouldBe(StatusCodes.Status401Unauthorized);
        Status(await M1aEndpoints.PutFavoriteAsync(
            productId, Context(null, "put"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status401Unauthorized);
        Status(await M1aEndpoints.DeleteFavoriteAsync(
            productId, Context(null, "delete"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status401Unauthorized);

        favorites.TotalCalls.ShouldBe(0);
        idempotency.Calls.ShouldBe(0);
    }

    [Fact(DisplayName = "PUT／DELETE：204 冪等、格式錯誤 404，同一把 key 不重做副作用")]
    public async Task Put_and_delete_have_the_frozen_status_and_idempotency_behavior()
    {
        var customerId = CustomerId.New();
        var sessions = new MemorySessionStore();
        var token = sessions.Issue(customerId);
        var favorites = new EndpointFavorites();
        var idempotency = new MemoryIdempotencyStore();
        var productId = ProductId.New();
        var cancellationToken = TestContext.Current.CancellationToken;

        Status(await M1aEndpoints.PutFavoriteAsync(
            productId.ToString(), Context(token, "same-put"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status204NoContent);
        Status(await M1aEndpoints.PutFavoriteAsync(
            productId.ToString(), Context(token, "same-put"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status204NoContent);
        favorites.AddCalls.ShouldBe(1);
        favorites.RowCount.ShouldBe(1);

        Status(await M1aEndpoints.DeleteFavoriteAsync(
            ProductId.New().ToString(), Context(token, "missing-delete"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status204NoContent);
        Status(await M1aEndpoints.DeleteFavoriteAsync(
            ProductId.New().ToString(), Context(token, "missing-delete-2"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status204NoContent);

        var addCalls = favorites.AddCalls;
        Status(await M1aEndpoints.PutFavoriteAsync(
            "bad-id", Context(token, "bad-put"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status404NotFound);
        Status(await M1aEndpoints.DeleteFavoriteAsync(
            "bad-id", Context(token, "bad-delete"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status404NotFound);
        favorites.AddCalls.ShouldBe(addCalls);

        favorites.AddError = new Error("catalog.product-not-found", "找不到商品。");
        Status(await M1aEndpoints.PutFavoriteAsync(
            ProductId.New().ToString(), Context(token, "hidden-put"), sessions, favorites, idempotency,
            cancellationToken)).ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact(DisplayName = "PUT：兩把 key 真正同時加入都 204 且只一列；同 key 處理中回 409")]
    public async Task Concurrent_puts_use_database_safe_semantics_and_in_flight_is_conflict()
    {
        var customerId = CustomerId.New();
        var sessions = new MemorySessionStore();
        var token = sessions.Issue(customerId);
        var favorites = new EndpointFavorites(concurrentAdds: 2);
        var idempotency = new MemoryIdempotencyStore();
        var productId = ProductId.New();
        var cancellationToken = TestContext.Current.CancellationToken;

        var results = await Task.WhenAll(
            M1aEndpoints.PutFavoriteAsync(
                productId.ToString(), Context(token, "parallel-a"), sessions, favorites, idempotency,
                cancellationToken),
            M1aEndpoints.PutFavoriteAsync(
                productId.ToString(), Context(token, "parallel-b"), sessions, favorites, idempotency,
                cancellationToken));

        results.Select(Status).ShouldAllBe(status => status == StatusCodes.Status204NoContent);
        favorites.AddCalls.ShouldBe(2);
        favorites.RowCount.ShouldBe(1);

        var inFlight = new MemoryIdempotencyStore(alwaysInFlight: true);
        Status(await M1aEndpoints.PutFavoriteAsync(
            productId.ToString(), Context(token, "in-flight"), sessions, favorites, inFlight,
            cancellationToken)).ShouldBe(StatusCodes.Status409Conflict);
    }

    [Fact(DisplayName = "GET：invalid cursor/limit 是 422，空頁形狀正確，預購價格與 campaignId 一次補齊")]
    public async Task Get_favorites_maps_validation_empty_page_and_preorder_pricing()
    {
        var customerId = CustomerId.New();
        var sessions = new MemorySessionStore();
        var token = sessions.Issue(customerId);
        var favorites = new EndpointFavorites();
        var campaigns = new FakeCampaignStorefront();
        var cancellationToken = TestContext.Current.CancellationToken;

        foreach (var (cursor, limit) in new[] { ((string?)null, (int?)0), (null, 101), ("junk", 20) })
        {
            Status(await M1aEndpoints.GetFavoritesAsync(
                Context(token, null), sessions, favorites, campaigns, cursor, limit,
                cancellationToken)).ShouldBe(StatusCodes.Status422UnprocessableEntity);
        }

        var emptyResult = await M1aEndpoints.GetFavoritesAsync(
            Context(token, null), sessions, favorites, campaigns, null, null, cancellationToken);
        Status(emptyResult).ShouldBe(StatusCodes.Status200OK);
        var empty = ((IValueHttpResult)emptyResult).Value.ShouldBeOfType<M1aEndpoints.ProductPageResponse>();
        empty.Items.ShouldBeEmpty();
        empty.NextCursor.ShouldBeNull();

        var productId = ProductId.New();
        var campaignId = CampaignId.New();
        var offerId = CampaignOfferId.New();
        favorites.Page = new CursorPage<StorefrontProductListItem>(
            [new(productId, "preorder", null, null, null, null, FulfillmentMode.Preorder)], null);
        campaigns.With(new StorefrontProductCampaign(
            productId,
            new StorefrontCampaignListItem(
                campaignId, "campaign", "Seoul", new DateOnly(2026, 9, 20),
                new DateOnly(2026, 9, 25), Now.AddDays(1), CampaignStatus.Open, true, null),
            Money.OfMajor(1_000, Currency.TWD),
            new Dictionary<SkuId, StorefrontCampaignSkuOffer>
            {
                [SkuId.New()] = new(offerId, campaignId, Money.OfMajor(1_000, Currency.TWD)),
            }));

        var pricedResult = await M1aEndpoints.GetFavoritesAsync(
            Context(token, null), sessions, favorites, campaigns, null, 20, cancellationToken);
        var priced = ((IValueHttpResult)pricedResult).Value
            .ShouldBeOfType<M1aEndpoints.ProductPageResponse>().Items.Single();
        priced.IsFavorited.ShouldBeTrue();
        priced.CampaignId.ShouldBe(campaignId);
        priced.PriceFrom.ShouldBe(Money.OfMajor(1_000, Currency.TWD));
    }

    private static int Status(IResult result) =>
        (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;

    private static DefaultHttpContext Context(string? sessionToken, string? idempotencyKey)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (sessionToken is not null)
        {
            context.Request.Headers.Cookie = $"gg_session={sessionToken}";
        }

        if (idempotencyKey is not null)
        {
            context.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        return context;
    }

    private sealed class MemorySessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionRecord> _sessions = [];

        public string Issue(CustomerId customerId)
        {
            var token = Guid.CreateVersion7().ToString("N");
            _sessions[token] = new SessionRecord(
                customerId.ToString(), SessionSubjectKind.Customer, TenantId.Default,
                null, Now, Now.AddDays(30));
            return token;
        }

        public Task<string> CreateAsync(
            string subjectId,
            SessionSubjectKind subjectKind,
            TenantId tenantId,
            string? role,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SessionRecord?> GetAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.GetValueOrDefault(token));

        public Task DeleteAsync(string token, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EndpointFavorites(int concurrentAdds = 0) : IStorefrontFavorites
    {
        private readonly HashSet<(CustomerId CustomerId, ProductId ProductId)> _rows = [];
        private readonly object _gate = new();
        private readonly TaskCompletionSource _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public CursorPage<StorefrontProductListItem> Page { get; set; } = new([], null);
        public Error? AddError { get; set; }
        public int AddCalls { get; private set; }
        public int TotalCalls { get; private set; }
        public int RowCount { get { lock (_gate) return _rows.Count; } }

        public async Task<Result> AddAsync(
            CustomerId customerId,
            ProductId productId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                AddCalls++;
                TotalCalls++;
            }

            if (AddError is { } error)
            {
                return Result.Failure(error);
            }

            if (concurrentAdds > 0)
            {
                if (Interlocked.Increment(ref _arrived) == concurrentAdds)
                {
                    _barrier.TrySetResult();
                }

                await _barrier.Task.WaitAsync(cancellationToken);
            }

            lock (_gate)
            {
                _rows.Add((customerId, productId));
            }

            return Result.Success();
        }

        public Task<Result> RemoveAsync(
            CustomerId customerId,
            ProductId productId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                TotalCalls++;
                _rows.Remove((customerId, productId));
            }

            return Task.FromResult(Result.Success());
        }

        public Task<Result<CursorPage<StorefrontProductListItem>>> ListAsync(
            CustomerId customerId,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
        {
            TotalCalls++;
            return Task.FromResult(limit is < 1 or > 100 || cursor == "junk"
                ? Result<CursorPage<StorefrontProductListItem>>.Failure(
                    "catalog.invalid-cursor", "cursor 無效或 limit 不在 1 到 100 之間。")
                : Result<CursorPage<StorefrontProductListItem>>.Success(Page));
        }

        public Task<IReadOnlySet<ProductId>> FindAsync(
            CustomerId customerId,
            IReadOnlyCollection<ProductId> productIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MemoryIdempotencyStore(bool alwaysInFlight = false) : IIdempotencyStore
    {
        private readonly Dictionary<(string Key, string Scope), Entry> _entries = [];
        private readonly object _gate = new();

        public int Calls { get; private set; }

        public Task<(IdempotencyOutcome Outcome, string? cachedResponse)> TryBeginAsync(
            string key,
            string scope,
            string requestHash,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Calls++;
                if (alwaysInFlight)
                {
                    return Value(IdempotencyOutcome.InFlight, null);
                }

                var identity = (key, scope);
                if (!_entries.TryGetValue(identity, out var entry))
                {
                    _entries[identity] = new Entry(requestHash, false);
                    return Value(IdempotencyOutcome.Proceed, null);
                }

                return entry.Hash != requestHash
                    ? Value(IdempotencyOutcome.KeyReusedWithDifferentPayload, null)
                    : Value(entry.Completed
                        ? IdempotencyOutcome.AlreadyCompleted
                        : IdempotencyOutcome.InFlight, "{}");
            }
        }

        public Task CompleteAsync(
            string key,
            string scope,
            string responseSnapshot,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var entry = _entries[(key, scope)];
                _entries[(key, scope)] = entry with { Completed = true };
            }

            return Task.CompletedTask;
        }

        public Task AbandonAsync(string key, string scope, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _entries.Remove((key, scope));
            }

            return Task.CompletedTask;
        }

        private static Task<(IdempotencyOutcome Outcome, string? cachedResponse)> Value(
            IdempotencyOutcome outcome,
            string? response) => Task.FromResult((outcome, response));

        private sealed record Entry(string Hash, bool Completed);
    }
}
