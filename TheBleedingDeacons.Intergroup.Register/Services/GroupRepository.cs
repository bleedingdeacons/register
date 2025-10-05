using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    
    public class GroupRepository : IGroupRepository
    {
        private static readonly ILogger Logger = AppLogger.ForContext<EmailTemplateService>();

        private readonly RegisterContext _context;
        private readonly CacheService _cache;

        private static class CacheKeys
        {
            public const string AllGroups = "all_groups";
            public static string GroupById(int id) => $"group_{id}";
            public static string GroupsByDay(string day) => $"groups_day_{day.ToLowerInvariant()}";
        }

        private TimeSpan GetCacheDuration(int baseMinutes = 15)
        {
            try
            {
                var connectivity = Connectivity.Current.NetworkAccess;
                return connectivity == NetworkAccess.Internet
                    ? TimeSpan.FromMinutes(baseMinutes / 3)  // Shorter when online
                    : TimeSpan.FromMinutes(baseMinutes * 2); // Longer when offline
            }
            catch
            {
                // Fallback if connectivity check fails
                return TimeSpan.FromMinutes(baseMinutes);
            }
        }

        public GroupRepository(RegisterContext context, CacheService cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<List<Group>> GetAllGroupsAsync()
        {
            return await _cache.GetOrSetAsync(
                CacheKeys.AllGroups,
                async () => await _context.Groups
                    .AsNoTracking()
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task<Group> SaveGroupAsync(Group group)
        {
            if (group == null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            Group savedGroup;

            // Check if this is a new group or an existing one
            if (group.ID == 0)
            {
                // New group - add it
                _context.Groups.Add(group);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedGroup = group;
            }
            else
            {
                // Existing group - update it
                var existingGroup = await _context.Groups.FindAsync(group.ID).ConfigureAwait(false);
                if (existingGroup == null)
                {
                    throw new InvalidOperationException($"Group with ID {group.ID} not found for update");
                }

                // Update properties
                _context.Entry(existingGroup).CurrentValues.SetValues(group);
                existingGroup.Updated = DateTime.Now;

                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedGroup = existingGroup;
            }

            // Invalidate relevant caches
            await InvalidateGroupCacheAsync(savedGroup.ID);

            // Also invalidate day-specific cache if we know the day
            if (!string.IsNullOrWhiteSpace(savedGroup.Day))
            {
                await InvalidateGroupsByDayCacheAsync(savedGroup.Day);
            }

            return savedGroup;
        }

        public async Task<Group?> GetGroupByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return await _cache.GetOrSetAsync(
                CacheKeys.GroupById(id),
                async () => await _context.Groups
                    .AsNoTracking()
                    .FirstOrDefaultAsync(g => g.ID == id)
                    .ConfigureAwait(false),
                GetCacheDuration(10)
            );
        }

        public async Task<Group?> GetGroupDirectlyAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return await _context.Groups.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<List<Group>> GetGroupsByDayAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
            {
                return new List<Group>();
            }

            var normalizedDay = day.Trim().ToLowerInvariant();

            return await _cache.GetOrSetAsync(
                CacheKeys.GroupsByDay(normalizedDay),
                async () => await _context.Groups
                    .AsNoTracking()
                    .Where(g => g.Day.ToLower() == normalizedDay)
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task InvalidateGroupCacheAsync(int id)
        {
            if (id <= 0)
            {
                return;
            }

            await _cache.RemoveAsync(CacheKeys.GroupById(id));

            // Also invalidate all groups cache since it contains this group
            await _cache.RemoveAsync(CacheKeys.AllGroups);

            // Note: We can't easily invalidate day-specific caches without knowing the day
            // Consider storing a mapping of group ID to day if this becomes important
        }

        public async Task InvalidateAllGroupsCacheAsync()
        {
            await _cache.RemoveAsync(CacheKeys.AllGroups);

            // Note: This doesn't invalidate day-specific or individual group caches
            // Consider using cache tags or patterns if your cache service supports it
        }

        public async Task InvalidateGroupsByDayCacheAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
            {
                return;
            }

            var normalizedDay = day.Trim().ToLowerInvariant();
            await _cache.RemoveAsync(CacheKeys.GroupsByDay(normalizedDay));
        }
    }
}
