using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Infrastructure.Persistence;
using FF.Infrastructure.Persistence.SQL;
using FF.Infrastructure.Persistence.SQL.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FF.Tests.Persistence;

public class UnitOfWorkTests : IDisposable
{
    private readonly FFDbContext _context;
    private readonly UnitOfWork _unitOfWork;

    public UnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<FFDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FFDbContext(options);
        _unitOfWork = new UnitOfWork(_context);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPlayerAddedViaUnitOfWork()
    {
        var player = Player.Create("CeeDee", "Lamb", Position.WR, "DAL", "sleeper-cdl");
        await _unitOfWork.Players.AddAsync(player);
        await _unitOfWork.SaveChangesAsync();

        var result = await _unitOfWork.Players.GetByIdAsync(player.Id);
        result.Should().NotBeNull();
        result!.FullName.Should().Be("CeeDee Lamb");
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsLeagueAndRosterTogether()
    {
        var league = League.Create("Test League", "sleeper-league-1", 2025, 12);
        await _unitOfWork.Leagues.AddAsync(league);
        await _unitOfWork.SaveChangesAsync();

        var roster = Roster.Create(league.Id, "Paul", "Paul's Team", "sleeper-roster-1");
        await _unitOfWork.Rosters.AddAsync(roster);
        await _unitOfWork.SaveChangesAsync();

        var rosters = await _unitOfWork.Rosters.GetByLeagueIdAsync(league.Id);
        rosters.Should().HaveCount(1);
        rosters[0].TeamName.Should().Be("Paul's Team");
    }

    public void Dispose() => _unitOfWork.Dispose();
}