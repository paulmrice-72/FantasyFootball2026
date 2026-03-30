// FF.Tests/Caching/CacheServiceTests.cs
using FF.Application.Common;
using FF.Application.Features.WaiverRecommendations.Queries;
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

    [Fact]
    public async Task VorpHandler_returns_cached_result_without_hitting_repo()
    {
        // Arrange
        var projRepo = Substitute.For<IPlayerProjectionRepository>();
        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        var vorpRepo = Substitute.For<IVorpRecommendationRepository>();
        var simRepo = Substitute.For<ISimulationResultRepository>();
        var cache = CreateService();

        var cachedResult = new List<VorpRecommendationDocument>
        {
            new() { PlayerId = "abc", PlayerName = "Test Player", Position = "WR", Vorp = 5.5m }
        };

        var cacheKey = CacheKeys.VorpRecommendations("league1", 2026, 1, null, 20);
        cache.Set(cacheKey, (IReadOnlyList<VorpRecommendationDocument>)cachedResult);

        var handler = new GetWaiverRecommendationsQueryHandler(
            projRepo, rosterRepo, vorpRepo, simRepo, cache);

        var query = new GetWaiverRecommendationsQuery("league1", 2026, 1, null, 20);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert — repo never called because cache hit
        result.Should().HaveCount(1);
        result[0].PlayerName.Should().Be("Test Player");
        await projRepo.DidNotReceive().GetByWeekAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VorpHandler_caches_result_on_cache_miss()
    {
        // Arrange
        var projRepo = Substitute.For<IPlayerProjectionRepository>();
        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        var vorpRepo = Substitute.For<IVorpRecommendationRepository>();
        var simRepo = Substitute.For<ISimulationResultRepository>();
        var cache = CreateService();

        // Return empty projections — handler returns early with []
        projRepo.GetByWeekAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var handler = new GetWaiverRecommendationsQueryHandler(
            projRepo, rosterRepo, vorpRepo, simRepo, cache);

        var query = new GetWaiverRecommendationsQuery("league1", 2026, 1, null, 20);

        // Act
        await handler.Handle(query, CancellationToken.None);

        // Assert — repo WAS called (cache miss triggered real load)
        await projRepo.Received(1).GetByWeekAsync(2026, 1, Arg.Any<CancellationToken>());
    }
}