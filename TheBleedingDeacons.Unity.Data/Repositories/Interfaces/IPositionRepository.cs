using TheBleedingDeacons.Unity.Data.Entities;

namespace TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

public interface IPositionRepository
{
    Task<List<Position>> GetAllAsync(CancellationToken ct = default);
    Task<Position?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Position?> GetByIdWithHoldersAsync(int id, CancellationToken ct = default);
    Task<List<Position>> GetFilledPositionsAsync(CancellationToken ct = default);
    Task<List<Position>> GetVacantPositionsAsync(CancellationToken ct = default);
    Task<List<Position>> SearchAsync(string searchTerm, CancellationToken ct = default);
}
