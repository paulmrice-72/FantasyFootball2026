namespace FF.Application.Interfaces.Persistence;

public interface IUnitOfWork : IDisposable
{
    IPlayerRepository Players { get; }
    ILeagueRepository Leagues { get; }
    IRosterRepository Rosters { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}