using TheBleedingDeacons.Intergroup.Register.Utilities;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

/// <summary>
/// Provides access to register data from the Unity WordPress API.
/// </summary>
public interface IUnityApiService
{
    /// <summary>
    /// Fetches all groups (with meetings expanded) and positions from the Unity API
    /// and returns them as a <see cref="RegisterData"/> suitable for import.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A <see cref="RegisterData"/> containing mapped groups and positions.</returns>
    Task<RegisterData> GetRegisterDataAsync(CancellationToken cancellationToken = default);
}