using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace MusicDatabase.Services
{
    /*--------------------------------------------------------------------------
     * CLASS MusicCache
     * Cache-aside wrapper around Redis (IDistributedCache) for the music pages.
     *
     * - GetOrCreateAsync(): return the cached copy, or load it from MySQL and
     *   cache it for 10 minutes.
     * - InvalidateAsync(): called after any database write. Every cache key
     *   includes a version ("v<version>:albums"); a write sets a new version,
     *   so all older keys are never read again and simply expire. This avoids
     *   having to find and delete every search-result key.
     * - If Redis is down, pages are read straight from MySQL and a warning is
     *   logged; the site keeps working. After a failure the cache is skipped
     *   for 30 seconds, so requests don't each wait for a Redis timeout.
     * Shared by all copies of the app, so a write on one copy is seen by all.
     *--------------------------------------------------------------------------
     */
    public class MusicCache
    {
        private const string VersionKey = "version";

        private static readonly DistributedCacheEntryOptions EntryOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
        };

        // after a Redis failure, don't try Redis again until this time:
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
        private DateTime _retryAtUtc = DateTime.MinValue;

        private readonly IDistributedCache _cache;
        private readonly ILogger<MusicCache> _logger;

        public MusicCache(IDistributedCache cache, ILogger<MusicCache> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        private bool CacheUsable => DateTime.UtcNow >= _retryAtUtc;

        // log one short warning and stop using Redis for RetryDelay
        private void CacheFailed(Exception ex, string action)
        {
            _retryAtUtc = DateTime.UtcNow + RetryDelay;
            _logger.LogWarning("Cache unavailable ({Error}) while {Action}; using the database only for {Seconds}s",
                ex.GetType().Name, action, RetryDelay.TotalSeconds);
        }

        /*----------------------------------------------------------------------
         * GetOrCreateAsync()
         * name: what is cached, e.g. "albums", "search:artist:zappa", "details:5"
         * load: reads the value from the database on a cache miss
         * A null result (e.g. album not found) is returned but not cached.
         *----------------------------------------------------------------------
         */
        public async Task<T> GetOrCreateAsync<T>(string name, Func<Task<T>> load) where T : class
        {
            if (!CacheUsable)
            {
                return await load();   // Redis failed recently: go straight to MySQL
            }

            string key;
            try
            {
                string version = await _cache.GetStringAsync(VersionKey) ?? "0";
                key = $"v{version}:{name}";

                string json = await _cache.GetStringAsync(key);
                if (json != null)
                {
                    _logger.LogDebug("Cache hit {Key}", key);
                    return JsonSerializer.Deserialize<T>(json);
                }
                _logger.LogDebug("Cache miss {Key}", key);
            }
            catch (Exception ex)
            {
                CacheFailed(ex, $"reading {name}");
                return await load();
            }

            T value = await load();
            if (value != null)
            {
                try
                {
                    await _cache.SetStringAsync(key, JsonSerializer.Serialize(value), EntryOptions);
                }
                catch (Exception ex)
                {
                    CacheFailed(ex, $"storing {name}");
                }
            }
            return value;
        }

        /*----------------------------------------------------------------------
         * InvalidateAsync()
         * Call after any change to albums or songs. If Redis is down the change
         * still succeeds; cached pages may then be stale for up to 10 minutes.
         *----------------------------------------------------------------------
         */
        public async Task InvalidateAsync()
        {
            // always try, even after a recent failure: a missed invalidation
            // means stale pages, which is worse than one slow request
            try
            {
                await _cache.SetStringAsync(VersionKey, Guid.NewGuid().ToString("N"));
                _retryAtUtc = DateTime.MinValue;   // Redis works: use it again
                _logger.LogInformation("Cache invalidated");
            }
            catch (Exception ex)
            {
                CacheFailed(ex, "invalidating");
                _logger.LogWarning("Cached pages may be stale for up to {Minutes} minutes",
                    EntryOptions.AbsoluteExpirationRelativeToNow.Value.TotalMinutes);
            }
        }
    }

    /*--------------------------------------------------------------------------
     * CLASS AlbumDetails
     * What the Music/Details page needs, cached as one entry.
     *--------------------------------------------------------------------------
     */
    public class AlbumDetails
    {
        public Models.Music Album { get; set; }
        public System.Collections.Generic.List<Models.Songs> Songs { get; set; }
    }
}
