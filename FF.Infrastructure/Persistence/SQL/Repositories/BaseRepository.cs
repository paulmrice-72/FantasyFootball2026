using FF.Application.Interfaces.Persistence;
using FF.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Persistence.SQL.Repositories;

public abstract class BaseRepository<T>(FFDbContext context) : IRepository<T> where T : Entity
{
    protected readonly FFDbContext Context = context;
    protected readonly DbSet<T> DbSet = context.Set<T>();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbSet.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking().ToListAsync(cancellationToken);

    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await DbSet.AddAsync(entity, cancellationToken);

    public void Update(T entity)
        => DbSet.Update(entity);

    public void Remove(T entity)
        => DbSet.Remove(entity);
}