using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IGroupRepository
    {
        Task<List<Group>> GetAllGroupsAsync();
        Task<Group?> GetGroupByIdAsync(int id);
        Task<List<Group>> GetGroupsByDayAsync(string day);
        Task<Group?> GetGroupDirectlyAsync(int id);
        Task<Group> SaveGroupAsync(Group group);
        Task InvalidateGroupCacheAsync(int id);
        Task InvalidateAllGroupsCacheAsync();
        Task InvalidateGroupsByDayCacheAsync(string day);
    }
}
