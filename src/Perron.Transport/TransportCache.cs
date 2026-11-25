using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Perron.Transport;

/// <summary>
/// Response cache of the client. Expiry is checked against <see cref="TimeProvider"/> (testable);
/// the inner <see cref="MemoryCache"/> bounds the size and evicts old entries.
/// </summary>
public sealed class TransportCache : IDisposable
{
    private readonly MemoryCache cache;
    private readonly TimeProvider timeProvider;

    public TransportCache(IOptions<TransportApiOptions> options, TimeProvider timeProvider)
    {
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.Value.CacheSizeLimit });
        this.timeProvider = timeProvider;
    }

    public bool TryGet<T>(string key, out T value)
        where T : class
    {
        if (cache.TryGetValue(key, out Entry? entry)
            && entry is { Value: T cached }
            && entry.ExpiresAt > timeProvider.GetUtcNow())
        {
            value = cached;
            return true;
        }

        value = null!;
        return false;
    }

    public void Set<T>(string key, T value, TimeSpan duration)
        where T : class
    {
        var entry = new Entry(value, timeProvider.GetUtcNow() + duration);
        cache.Set(key, entry, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = duration });
    }

    public void Dispose() => cache.Dispose();

    private sealed record Entry(object Value, DateTimeOffset ExpiresAt);
}
