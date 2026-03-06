using FF.Application.Interfaces.Persistence;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly FFDbContext _context;
    private bool _disposed;

    public IPlayerRepository Players { get; }
    public ILeagueRepository Leagues { get; }
    public IRosterRepository Rosters { get; }

    public UnitOfWork(FFDbContext context)
    {
        _context = context;
        Players = new PlayerRepository(context);
        Leagues = new LeagueRepository(context);
        Rosters = new RosterRepository(context);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

    public void Dispose()
    {
        if (!_disposed)
        {
            _context.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}