using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IPositionRepository
    {
        Task<List<Position>> GetAllPositionsAsync();
        Task<Position?> GetPositionByIdAsync(int id);
        Task<List<Position>> GetPositionsByDayAsync(string day);
        Task<Position?> GetPositionDirectlyAsync(int id);
        Task<Position> SavePositionAsync(Position Position);
        Task InvalidatePositionCacheAsync(int id);
        Task InvalidateAllPositionsCacheAsync();
        Task InvalidatePositionsByDayCacheAsync(string day);
    }
}
