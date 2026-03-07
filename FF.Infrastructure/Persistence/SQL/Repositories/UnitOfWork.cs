using FF.Application.Interfaces.Persistence;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public class UnitOfWork(FFDbContext context) : IUnitOfWork
{
    private readonly FFDbContext _context = context;
    private bool _disposed;

    public IPlayerRepository Players { get; } = new PlayerRepository(context);
    public ILeagueRepository Leagues { get; } = new LeagueRepository(context);
    public IRosterRepository Rosters { get; } = new RosterRepository(context);

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