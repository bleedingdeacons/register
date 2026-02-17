using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{

    public class MeetingRepository : IMeetingRepository
    {
        private static readonly ILogger Logger = AppLogger.ForContext<MeetingRepository>();

        private readonly RegisterContext _context;
        private readonly CacheService _cache;

        private static class CacheKeys
        {
            public const string AllMeetings = "all_meetings";
            public static string MeetingById(int id) => $"meeting_{id}";
            public static string MeetingsByDay(string day) => $"meetings_day_{day.ToLowerInvariant()}";
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

        public MeetingRepository(RegisterContext context, CacheService cache)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<List<Meeting>> GetAllMeetingsAsync()
        {
            return await _cache.GetOrSetAsync(
                CacheKeys.AllMeetings,
                async () => await _context.Meetings
                    .AsNoTracking()
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task<Meeting> SaveMeetingAsync(Meeting meeting)
        {
            if (meeting == null)
            {
                throw new ArgumentNullException(nameof(meeting));
            }

            Meeting savedMeeting;

            // Check if this is a new meeting or an existing one
            if (meeting.ID == 0)
            {
                // New meeting - add it
                _context.Meetings.Add(meeting);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedMeeting = meeting;
            }
            else
            {
                // Existing meeting - update it
                var existingMeeting = await _context.Meetings.FindAsync(meeting.ID).ConfigureAwait(false);
                if (existingMeeting == null)
                {
                    throw new InvalidOperationException($"Meeting with ID {meeting.ID} not found for update");
                }

                // Update properties
                _context.Entry(existingMeeting).CurrentValues.SetValues(meeting);
                existingMeeting.Updated = DateTime.Now;

                await _context.SaveChangesAsync().ConfigureAwait(false);
                savedMeeting = existingMeeting;
            }

            // Invalidate relevant caches
            await InvalidateMeetingCacheAsync(savedMeeting.ID);

            // Also invalidate day-specific cache if we know the day
            if (!string.IsNullOrWhiteSpace(savedMeeting.Day))
            {
                await InvalidateMeetingsByDayCacheAsync(savedMeeting.Day);
            }

            return savedMeeting;
        }

        public async Task<Meeting?> GetMeetingByIdAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return await _cache.GetOrSetAsync(
                CacheKeys.MeetingById(id),
                async () => await _context.Meetings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.ID == id)
                    .ConfigureAwait(false),
                GetCacheDuration(10)
            );
        }

        public async Task<Meeting?> GetMeetingDirectlyAsync(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            return await _context.Meetings.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<List<Meeting>> GetMeetingsByDayAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
            {
                return new List<Meeting>();
            }

            var normalizedDay = day.Trim().ToLowerInvariant();

            return await _cache.GetOrSetAsync(
                CacheKeys.MeetingsByDay(normalizedDay),
                async () => await _context.Meetings
                    .AsNoTracking()
                    .Where(m => m.Day.ToLower() == normalizedDay)
                    .ToListAsync()
                    .ConfigureAwait(false),
                GetCacheDuration(15)
            );
        }

        public async Task InvalidateMeetingCacheAsync(int id)
        {
            if (id <= 0)
            {
                return;
            }

            await _cache.RemoveAsync(CacheKeys.MeetingById(id));

            // Also invalidate all meetings cache since it contains this meeting
            await _cache.RemoveAsync(CacheKeys.AllMeetings);
        }

        public async Task InvalidateAllMeetingsCacheAsync()
        {
            await _cache.RemoveAsync(CacheKeys.AllMeetings);
        }

        public async Task InvalidateMeetingsByDayCacheAsync(string day)
        {
            if (string.IsNullOrWhiteSpace(day))
            {
                return;
            }

            var normalizedDay = day.Trim().ToLowerInvariant();
            await _cache.RemoveAsync(CacheKeys.MeetingsByDay(normalizedDay));
        }
    }
}
