namespace FF.Application.Interfaces.Services;

public interface ICacheService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan? absoluteExpiry = null);
    void Remove(string key);
}