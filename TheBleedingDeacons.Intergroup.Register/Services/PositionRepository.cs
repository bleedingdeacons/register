using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class PositionRepository : IPositionRepository
    {
        private static readonly ILogger Logger = AppLogger.ForContext<PositionRepository>();

        private readonly RegisterContext _context;
        private readonly CacheService _cache;

        private static class CacheKeys
        {
            public const string AllPositions = "all_positions";
            public static string PositionById(int id) => $"position_{id}";
            public static string PositionsByDay(string day) => $"positions_day_{day.ToLowerInvariant()}";
        }

        private TimeSpan GetCacheDuration(int baseMinutes = 15)
        {
            try
            {
                var connectivity = Connectivity.Current.NetworkAccess;
                return connectivity == NetworkAccess.Internet
                    ? TimeSpan.FromMinutes(baseMinutes / 3)
                    : TimeSpan.FromMinutes(baseMinutes * 2);
            }
            catch
            {
                return TimeSpan.FromMinutes(baseMinutes);
            }
        }

        public PositionRepository(RegisterContext context, CacheService cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<List<Position>> GetAllPositionsAsync()
        {
            return await _cache.GetOrSetAsync(
                CacheKeys.AllPositions,
                async () => await _context.Positions
                    .AsNoTracking()
                    .OrderBy(p => p.PositionName)
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task<Position?> GetPositionByIdAsync(int id)
        {
            if (id <= 0)
                return null;

            return await _cache.GetOrSetAsync(
                CacheKeys.PositionById(id),
                async () => await _context.Positions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ID == id)
                    .ConfigureAwait(false),
                GetCacheDuration(10)
            );
        }

        public async Task<List<Position>> GetPositionsByDayAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
                return new List<Position>();

            var normalizedDay = day.Trim().ToLowerInvariant();

            return await _cache.GetOrSetAsync(
                CacheKeys.PositionsByDay(normalizedDay),
                async () => await _context.Positions
                    .AsNoTracking()
                    .Where(p => p.PositionDuration != null && p.PositionDuration.Contains(day))
                    .OrderBy(p => p.PositionName)
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task<Position?> GetPositionDirectlyAsync(int id)
        {
            if (id <= 0)
                return null;

            return await _context.Positions.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<Position> SavePositionAsync(Position position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));

            Position savedPosition;

            if (position.ID == 0)
            {
                position.Updated = DateTime.UtcNow;
                _context.Positions.Add(position);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedPosition = position;
            }
            else
            {
                var existingPosition = await _context.Positions.FindAsync(position.ID).ConfigureAwait(false);
                if (existingPosition == null)
                    throw new InvalidOperationException($"Position with ID {position.ID} not found for update");

                _context.Entry(existingPosition).CurrentValues.SetValues(position);
                existingPosition.Updated = DateTime.UtcNow;

                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedPosition = existingPosition;
            }

            await InvalidatePositionCacheAsync(savedPosition.ID);
            await InvalidateAllPositionsCacheAsync();

            Logger.Information("Successfully saved position {Id}", savedPosition.ID);
            return savedPosition;
        }

        public async Task InvalidatePositionCacheAsync(int id)
        {
            if (id <= 0)
                return;

            await _cache.RemoveAsync(CacheKeys.PositionById(id));
            await _cache.RemoveAsync(CacheKeys.AllPositions);
        }

        public async Task InvalidateAllPositionsCacheAsync()
        {
            await _cache.RemoveAsync(CacheKeys.AllPositions);
        }

        public async Task InvalidatePositionsByDayCacheAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
                return;

            var normalizedDay = day.Trim().ToLowerInvariant();
            await _cache.RemoveAsync(CacheKeys.PositionsByDay(normalizedDay));
        }
    }
}