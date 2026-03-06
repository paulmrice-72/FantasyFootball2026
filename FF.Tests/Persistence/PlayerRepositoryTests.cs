using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Infrastructure.Persistence;
using FF.Infrastructure.Persistence.SQL;
using FF.Infrastructure.Persistence.SQL.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FF.Tests.Persistence;

public class PlayerRepositoryTests : IDisposable
{
    private readonly FFDbContext _context;
    private readonly PlayerRepository _repository;

    public PlayerRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FFDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // unique per test class
            .Options;

        _context = new FFDbContext(options);
        _repository = new PlayerRepository(_context);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPlayer_WhenExists()
    {
        var player = Player.Create("Justin", "Jefferson", Position.WR, "MIN", "sleeper-123");
        await _repository.AddAsync(player);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByIdAsync(player.Id);

        result.Should().NotBeNull();
        result!.FullName.Should().Be("Justin Jefferson");
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllPlayers()
    {
        await _repository.AddAsync(Player.Create("Patrick", "Mahomes", Position.QB, "KC", null));
        await _repository.AddAsync(Player.Create("Tyreek", "Hill", Position.WR, "MIA", null));
        await _context.SaveChangesAsync();

        var result = await _repository.GetAllAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByPositionAsync_ReturnsOnlyMatchingPosition()
    {
        await _repository.AddAsync(Player.Create("Patrick", "Mahomes", Position.QB, "KC", null));
        await _repository.AddAsync(Player.Create("Josh", "Allen", Position.QB, "BUF", null));
        await _repository.AddAsync(Player.Create("Tyreek", "Hill", Position.WR, "MIA", null));
        await _context.SaveChangesAsync();

        var result = await _repository.GetByPositionAsync(Position.QB);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.Position.Should().Be(Position.QB));
    }

    [Fact]
    public async Task GetBySleeperIdAsync_ReturnsPlayer_WhenSleeperIdMatches()
    {
        await _repository.AddAsync(Player.Create("Justin", "Jefferson", Position.WR, "MIN", "sleeper-999"));
        await _context.SaveChangesAsync();

        var result = await _repository.GetBySleeperIdAsync("sleeper-999");

        result.Should().NotBeNull();
        result!.LastName.Should().Be("Jefferson");
    }

    [Fact]
    public async Task Remove_DeletesPlayer()
    {
        var player = Player.Create("Player", "ToDelete", Position.K, "NE", null);
        await _repository.AddAsync(player);
        await _context.SaveChangesAsync();

        _repository.Remove(player);
        await _context.SaveChangesAsync();

        var result = await _repository.GetByIdAsync(player.Id);
        result.Should().BeNull();
    }

    public void Dispose() => _context.Dispose();
}