using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class CacheService
    {
        private static readonly ILogger Logger = AppLogger.ForContext<CacheService>();

        private readonly IMemoryCache _cache;
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30);

        public CacheService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> getItem, TimeSpan? expiration = null)
        {
            if (_cache.TryGetValue(key, out T cachedValue))
            {
                return cachedValue;
            }

            var item = await getItem();
            _cache.Set(key, item, expiration ?? _defaultExpiration);
            return item;
        }

        public async Task RemoveAsync(string key)
        {
            await Task.Run(() => _cache.Remove(key));
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
        }

        public async Task ClearAsync()
        {
            await Task.Run(() =>
            {
                if (_cache is MemoryCache memCache)
                {
                    memCache.Compact(1.0);
                }
            });
        }

        public void Clear()
        {
            if (_cache is MemoryCache memCache)
            {
                memCache.Compact(1.0);
            }
        }
    }
}