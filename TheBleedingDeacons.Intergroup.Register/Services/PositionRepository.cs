using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services.Repositories
{
    public class PositionRepository : IPositionRepository
    {
        private static readonly ILogger Logger = AppLogger.ForContext<PositionRepository>();

        private readonly RegisterContext _context;
        private readonly IMemoryCache _cache;        

        // Cache keys
        private const string ALL_POSITIONS_CACHE_KEY = "all_positions";
        private const string POSITION_BY_ID_CACHE_KEY = "position_by_id_{0}";
        private const string POSITIONS_BY_DAY_CACHE_KEY = "positions_by_day_{0}";

        // Cache expiration times
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);

        public PositionRepository(RegisterContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<List<Position>> GetAllPositionsAsync()
        {
            try
            {
                if (_cache.TryGetValue(ALL_POSITIONS_CACHE_KEY, out List<Position>? cachedPositions))
                {
                    Logger.Debug("Retrieved all positions from cache");
                    return cachedPositions!;
                }

                var positions = await _context.Positions
                    .OrderBy(p => p.PositionName)
                    .ToListAsync();

                _cache.Set(ALL_POSITIONS_CACHE_KEY, positions, _cacheExpiration);
                Logger.Debug("Retrieved {Count} positions from database and cached", positions.Count);

                return positions;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error retrieving all positions");
                throw;
            }
        }

        public async Task<Position?> GetPositionByIdAsync(int id)
        {
            try
            {
                var cacheKey = string.Format(POSITION_BY_ID_CACHE_KEY, id);

                if (_cache.TryGetValue(cacheKey, out Position? cachedPosition))
                {
                    Logger.Debug("Retrieved position {Id} from cache", id);
                    return cachedPosition;
                }

                var position = await _context.Positions
                    .FirstOrDefaultAsync(p => p.ID == id);

                if (position != null)
                {
                    _cache.Set(cacheKey, position, _cacheExpiration);
                    Logger.Debug("Retrieved position {Id} from database and cached", id);
                }

                return position;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error retrieving position with ID {Id}", id);
                throw;
            }
        }

        public async Task<List<Position>> GetPositionsByDayAsync(string day)
        {
            try
            {
                var cacheKey = string.Format(POSITIONS_BY_DAY_CACHE_KEY, day?.ToLowerInvariant());

                if (_cache.TryGetValue(cacheKey, out List<Position>? cachedPositions))
                {
                    Logger.Debug("Retrieved positions for day {Day} from cache", day);
                    return cachedPositions!;
                }

                // Note: Since the Position model doesn't have a Day property directly,
                // this might need to be adjusted based on your business logic.
                // This is a placeholder implementation - you may need to join with Groups
                // or implement day-based filtering differently.
                var positions = await _context.Positions
                    .Where(p => p.PositionDuration != null && p.PositionDuration.Contains(day ?? ""))
                    .OrderBy(p => p.PositionName)
                    .ToListAsync();

                _cache.Set(cacheKey, positions, _cacheExpiration);
                Logger.Debug("Retrieved {Count} positions for day {Day} from database and cached", positions.Count, day);

                return positions;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error retrieving positions for day {Day}", day);
                throw;
            }
        }

        public async Task<Position?> GetPositionDirectlyAsync(int id)
        {
            try
            {
                // This method bypasses cache and goes directly to the database
                var position = await _context.Positions
                    .FirstOrDefaultAsync(p => p.ID == id);

                Logger.Debug("Retrieved position {Id} directly from database", id);
                return position;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error retrieving position {Id} directly from database", id);
                throw;
            }
        }

        public async Task<Position> SavePositionAsync(Position position)
        {
            try
            {
                if (position.ID == 0)
                {
                    // New position
                    position.Updated = DateTime.UtcNow;
                    _context.Positions.Add(position);
                    Logger.Debug("Adding new position");
                }
                else
                {
                    // Update existing position
                    position.Updated = DateTime.UtcNow;
                    _context.Positions.Update(position);
                    Logger.Debug("Updating position {Id}", position.ID);
                }

                await _context.SaveChangesAsync();

                // Invalidate relevant caches
                await InvalidatePositionCacheAsync(position.ID);
                await InvalidateAllPositionsCacheAsync();

                // If position duration contains day information, invalidate day-based caches
                if (!string.IsNullOrEmpty(position.PositionDuration))
                {
                    // This is a simplified approach - you might need more sophisticated day extraction
                    var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
                    foreach (var day in days)
                    {
                        if (position.PositionDuration.Contains(day, StringComparison.OrdinalIgnoreCase))
                        {
                            await InvalidatePositionsByDayCacheAsync(day);
                        }
                    }
                }

                Logger.Information("Successfully saved position {Id}", position.ID);
                return position;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving position {Id}", position.ID);
                throw;
            }
        }

        public Task InvalidatePositionCacheAsync(int id)
        {
            try
            {
                var cacheKey = string.Format(POSITION_BY_ID_CACHE_KEY, id);
                _cache.Remove(cacheKey);
                Logger.Debug("Invalidated cache for position {Id}", id);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error invalidating cache for position {Id}", id);
                throw;
            }
        }

        public Task InvalidateAllPositionsCacheAsync()
        {
            try
            {
                _cache.Remove(ALL_POSITIONS_CACHE_KEY);
                Logger.Debug("Invalidated all positions cache");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error invalidating all positions cache");
                throw;
            }
        }

        public Task InvalidatePositionsByDayCacheAsync(string day)
        {
            try
            {
                var cacheKey = string.Format(POSITIONS_BY_DAY_CACHE_KEY, day?.ToLowerInvariant());
                _cache.Remove(cacheKey);
                Logger.Debug("Invalidated positions cache for day {Day}", day);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error invalidating positions cache for day {Day}", day);
                throw;
            }
        }
    }
}

