using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace FF.Infrastructure.Services;

public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(30);

    public T? Get<T>(string key)
    {
        cache.TryGetValue(key, out T? value);
        return value;
    }

    public void Set<T>(string key, T value, TimeSpan? absoluteExpiry = null)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiry ?? DefaultExpiry
        };
        cache.Set(key, value, options);
    }

    public void Remove(string key) => cache.Remove(key);
}