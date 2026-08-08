using Microsoft.Extensions.Caching.Memory;
using TheBleedingDeacons.Intergroup.Register.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Thin wrapper over <see cref="IMemoryCache"/>. Small enough to read in a
/// minute, but it has one behaviour that is easy to misread — see
/// <see cref="GetOrSetAsync_ReRunsTheFactoryWhenTheCachedValueIsNull"/>.
/// </summary>
public class CacheServiceTests
{
	private static CacheService NewCache(out MemoryCache backing)
	{
		backing = new MemoryCache(new MemoryCacheOptions());
		return new CacheService(backing);
	}

	[Fact]
	public async Task GetOrSetAsync_RunsTheFactoryOnceAndCachesTheResult()
	{
		var cache = NewCache(out _);
		var calls = 0;

		Task<string> Factory()
		{
			calls++;
			return Task.FromResult("value");
		}

		Assert.Equal("value", await cache.GetOrSetAsync("k", Factory));
		Assert.Equal("value", await cache.GetOrSetAsync("k", Factory));
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task GetOrSetAsync_KeepsEntriesForDifferentKeysApart()
	{
		var cache = NewCache(out _);

		Assert.Equal("a", await cache.GetOrSetAsync("ka", () => Task.FromResult("a")));
		Assert.Equal("b", await cache.GetOrSetAsync("kb", () => Task.FromResult("b")));
		Assert.Equal("a", await cache.GetOrSetAsync("ka", () => Task.FromResult("changed")));
	}

	[Fact]
	public async Task GetOrSetAsync_ReRunsTheFactoryWhenTheCachedValueIsNull()
	{
		// GetOrSetAsync tests `cachedValue is not null`, so a cached null is
		// indistinguishable from a cache miss and the factory runs every time.
		// Pinning it because it is either a deliberate "don't cache absence"
		// choice or a hole — and nothing in the code says which.
		var cache = NewCache(out _);
		var calls = 0;

		Task<string?> Factory()
		{
			calls++;
			return Task.FromResult<string?>(null);
		}

		await cache.GetOrSetAsync("k", Factory);
		await cache.GetOrSetAsync("k", Factory);

		Assert.Equal(2, calls);
	}

	[Fact]
	public async Task GetOrSetAsync_HonoursAnExplicitExpiry()
	{
		var cache = NewCache(out var backing);

		await cache.GetOrSetAsync("k", () => Task.FromResult("value"), TimeSpan.FromMilliseconds(1));

		// MemoryCache evaluates expiry on read; give the clock room to move.
		await Task.Delay(30);

		Assert.False(backing.TryGetValue("k", out string? _));
	}

	[Fact]
	public async Task Remove_EvictsASingleEntry()
	{
		var cache = NewCache(out _);
		await cache.GetOrSetAsync("k", () => Task.FromResult("value"));

		cache.Remove("k");

		var calls = 0;
		await cache.GetOrSetAsync("k", () => { calls++; return Task.FromResult("fresh"); });
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task RemoveAsync_EvictsASingleEntry()
	{
		var cache = NewCache(out var backing);
		await cache.GetOrSetAsync("k", () => Task.FromResult("value"));

		await cache.RemoveAsync("k");

		Assert.False(backing.TryGetValue("k", out string? _));
	}

	[Fact]
	public async Task Clear_EmptiesTheWholeCache()
	{
		var cache = NewCache(out var backing);
		await cache.GetOrSetAsync("a", () => Task.FromResult("1"));
		await cache.GetOrSetAsync("b", () => Task.FromResult("2"));

		cache.Clear();

		Assert.False(backing.TryGetValue("a", out string? _));
		Assert.False(backing.TryGetValue("b", out string? _));
	}

	[Fact]
	public async Task ClearAsync_EmptiesTheWholeCache()
	{
		var cache = NewCache(out var backing);
		await cache.GetOrSetAsync("a", () => Task.FromResult("1"));

		await cache.ClearAsync();

		Assert.False(backing.TryGetValue("a", out string? _));
	}

	[Fact]
	public async Task GetOrSetAsync_LetsAFactoryFailurePropagate()
	{
		// The caller needs to know the fetch failed; swallowing it here would
		// cache nothing and report success.
		var cache = NewCache(out _);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => cache.GetOrSetAsync<string>("k", () => throw new InvalidOperationException("boom")));
	}
}
