using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

public interface IIntergroupMeetingRepository
{
    Task<List<IntergroupMeeting>> GetAllAsync(CancellationToken ct = default);
    Task<IntergroupMeeting?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<IntergroupMeeting>> GetByDateRangeAsync(string dateFrom, string dateTo, CancellationToken ct = default);
    Task<List<IntergroupMeeting>> SearchAsync(string searchTerm, CancellationToken ct = default);
}
