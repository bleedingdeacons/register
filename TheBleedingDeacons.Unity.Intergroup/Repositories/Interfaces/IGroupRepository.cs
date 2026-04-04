using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

public interface IGroupRepository
{
    Task<List<Group>> GetAllAsync(CancellationToken ct = default);
    Task<Group?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Group?> GetByIdWithMembersAsync(int id, CancellationToken ct = default);
    Task<Group?> GetByIdWithMeetingsAsync(int id, CancellationToken ct = default);
    Task<List<Group>> SearchAsync(string searchTerm, CancellationToken ct = default);
    Task<List<Group>> GetByDistrictAsync(int districtId, CancellationToken ct = default);
}
