// FF.Tests/Caching/CacheServiceTests.cs
using FF.Application.Common;
using FF.Application.Features.WaiverRecommendations.Queries;
using FF.Application.Features.WaiverRecommendations.Queries.GetWaiverRecommendations;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using FluentAssertions;
using MathNet.Numerics;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using static Azure.Core.HttpHeader;

namespace FF.Tests.Caching;

public class CacheServiceTests
{
    private static MemoryCacheService CreateService()
    {
        var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        return new MemoryCacheService(cache);
    }

    [Fact]
    public void Get_returns_null_when_key_not_set()
    {
        var sut = CreateService();

        var result = sut.Get<string>("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public void Set_then_Get_returns_value()
    {
        var sut = CreateService();

        sut.Set("my-key", "hello");
        var result = sut.Get<string>("my-key");

        result.Should().Be("hello");
    }

    [Fact]
    public void Remove_after_Set_returns_null()
    {
        var sut = CreateService();

        sut.Set("my-key", 42);
        sut.Remove("my-key");

        sut.Get<int?>("my-key").Should().BeNull();
    }

    [Fact]
    public void Set_with_short_expiry_expires_entry()
    {
        var sut = CreateService();

        sut.Set("expiring-key", "value", TimeSpan.FromMilliseconds(50));
        Thread.Sleep(100);

        sut.Get<string>("expiring-key").Should().BeNull();
    }

    // ── FAN-118 ──────────────────────────────────────────────────────────────
    // These two exercise the cache through the waiver handler. That handler used to
    // take five dependencies and compute VORP itself; it now takes two and only reads
    // the stored board, so the collaborator being asserted against is the VORP
    // repository rather than the projection repository.

    private static VorpRecommendationDocument Available(string playerId, string name) =>
        new()
        {
            SleeperLeagueId = "league1",
            PlayerId        = playerId,
            PlayerName      = name,
            Position        = "WR",
            Season          = 2026,
            Week            = 1,
            IsRostered      = false,
            Vorp            = 5.5m
        };

    [Fact]
    public async Task VorpHandler_returns_cached_result_without_hitting_repo()
    {
        var vorpRepo = Substitute.For<IVorpRecommendationRepository>();
        var cache = CreateService();

        var cachedResult = new List<VorpRecommendationDocument> { Available("abc", "Test Player") };

        var cacheKey = CacheKeys.VorpRecommendations("league1", 2026, 1, null, 20);
        cache.Set(cacheKey, (IReadOnlyList<VorpRecommendationDocument>)cachedResult);

        var handler = new GetWaiverRecommendationsQueryHandler(vorpRepo, cache);
        var query = new GetWaiverRecommendationsQuery("league1", 2026, 1, null, 20);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].PlayerName.Should().Be("Test Player");

        await vorpRepo.DidNotReceive().GetByWeekAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VorpHandler_caches_result_on_cache_miss()
    {
        var vorpRepo = Substitute.For<IVorpRecommendationRepository>();
        var cache = CreateService();

        IReadOnlyList<VorpRecommendationDocument> board = [Available("abc", "Test Player")];

        vorpRepo.GetByWeekAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(board);

        var handler = new GetWaiverRecommendationsQueryHandler(vorpRepo, cache);
        var query = new GetWaiverRecommendationsQuery("league1", 2026, 1, null, 20);

        // Twice: the first call populates the cache, the second should be served from it.
        // Asserting the repository was hit exactly once is what actually demonstrates
        // caching — the previous version only proved the repository was reached at all.
        var first  = await handler.Handle(query, CancellationToken.None);
        var second = await handler.Handle(query, CancellationToken.None);

        first.Should().HaveCount(1);
        second.Should().HaveCount(1);

        await vorpRepo.Received(1).GetByWeekAsync(
            "league1", 2026, 1, null, 80, Arg.Any<CancellationToken>());
    }
}