using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IMeetingRepository
    {
        Task<List<Meeting>> GetAllMeetingsAsync();
        Task<Meeting?> GetMeetingByIdAsync(int id);
        Task<List<Meeting>> GetMeetingsByDayAsync(string day);
        Task<Meeting?> GetMeetingDirectlyAsync(int id);
        Task<Meeting> SaveMeetingAsync(Meeting meeting);
        Task InvalidateMeetingCacheAsync(int id);
        Task InvalidateAllMeetingsCacheAsync();
        Task InvalidateMeetingsByDayCacheAsync(string day);
    }
}
