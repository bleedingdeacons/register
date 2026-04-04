using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

public interface IMemberRepository
{
    Task<List<Member>> GetAllAsync(CancellationToken ct = default);
    Task<Member?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<Member>> GetGsrsAsync(CancellationToken ct = default);
    Task<List<Member>> GetByHomeGroupIdAsync(int groupId, CancellationToken ct = default);
    Task<List<Member>> GetByPositionIdAsync(int positionId, CancellationToken ct = default);
    Task<List<Member>> SearchAsync(string searchTerm, CancellationToken ct = default);
}
