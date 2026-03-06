using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

public interface IMeetingRepository
{
    Task<List<Meeting>> GetAllAsync(CancellationToken ct = default);
    Task<Meeting?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Meeting>> GetByGroupIdAsync(int groupId, CancellationToken ct = default);
    Task<List<Meeting>> GetByDayAsync(int day, CancellationToken ct = default);
    Task<List<Meeting>> GetOnlineMeetingsAsync(CancellationToken ct = default);
    Task<List<Meeting>> SearchAsync(string searchTerm, CancellationToken ct = default);
}
