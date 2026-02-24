using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Thin wrapper around <see cref="UnityRestSharp"/> write operations that automatically
/// enqueues any call that fails due to a network error or when the device is offline.
///
/// Read-only calls (GetGroups, GetPositions, GetMembers, etc.) are NOT queued —
/// they should fall back to the local SQLite cache via the normal code paths.
///
/// Queued write operations:
///   • RegisterGroupAsync      (intergroup meeting group registration)
///   • UnregisterGroupAsync
///   • RegisterOfficerAsync    (intergroup meeting officer registration)
///   • UnregisterOfficerAsync
///   • UpdateMemberAsync
/// </summary>
public sealed class QueueingUnityApiService : IDisposable
{
    // --------------------------------------------------------------- constants
    private static readonly ILogger Logger = AppLogger.ForContext<QueueingUnityApiService>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // --------------------------------------------------------------- fields
    private readonly IConfigurationService _configService;
    private readonly IApiQueueService _queue;
    private UnityRestSharp? _innerClient;
    private string? _cachedBaseUrl;
    private bool _disposed;

    // --------------------------------------------------------------- ctor

    public QueueingUnityApiService(
        IConfigurationService configService,
        IApiQueueService queue)
    {
        _configService = configService;
        _queue = queue;
    }

    // --------------------------------------------------------------- public write operations

    /// <summary>
    /// Registers a group/GSR as an attendee of an intergroup meeting.
    /// Enqueues the call if the device is offline or the request fails.
    /// </summary>
    public async Task<ApiResponse<IntergroupMeetingRegistration>> RegisterGroupAsync(
        int intergroupMeetingId,
        int groupId,
        int memberId,
        string gsrName,
        bool gsrProxy = false,
        string? gsrProxyName = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline())
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/register-group");
            var payload = Serialize(new
            {
                group_id = groupId,
                member_id = memberId,
                gsr_name = gsrName,
                gsr_proxy = gsrProxy,
                gsr_proxy_name = gsrProxyName ?? string.Empty
            });

            Logger.Information("Device offline – queuing RegisterGroup for meeting {Id}", intergroupMeetingId);
            await _queue.EnqueueAsync("RegisterGroup", url, "POST", payload, cancellationToken);
            return OfflineQueued<IntergroupMeetingRegistration>();
        }

        var client = await GetClientAsync();
        var response = await client.RegisterGroupAsync(
            intergroupMeetingId, groupId, memberId, gsrName, gsrProxy, gsrProxyName, cancellationToken);

        if (!response.Success && IsTransientError(response))
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/register-group");
            var payload = Serialize(new
            {
                group_id = groupId,
                member_id = memberId,
                gsr_name = gsrName,
                gsr_proxy = gsrProxy,
                gsr_proxy_name = gsrProxyName ?? string.Empty
            });

            Logger.Warning("RegisterGroup failed ({Code}) – queuing for retry", response.Error?.Code);
            await _queue.EnqueueAsync("RegisterGroup", url, "POST", payload, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Unregisters a group/GSR from an intergroup meeting.
    /// Enqueues the call if the device is offline or the request fails.
    /// </summary>
    public async Task<ApiResponse<IntergroupMeetingRegistration>> UnregisterGroupAsync(
        int intergroupMeetingId,
        int groupId,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline())
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/unregister-group");
            var payload = Serialize(new { group_id = groupId });

            Logger.Information("Device offline – queuing UnregisterGroup for meeting {Id}", intergroupMeetingId);
            await _queue.EnqueueAsync("UnregisterGroup", url, "POST", payload, cancellationToken);
            return OfflineQueued<IntergroupMeetingRegistration>();
        }

        var client = await GetClientAsync();
        var response = await client.UnregisterGroupAsync(intergroupMeetingId, groupId, cancellationToken);

        if (!response.Success && IsTransientError(response))
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/unregister-group");
            var payload = Serialize(new { group_id = groupId });

            Logger.Warning("UnregisterGroup failed ({Code}) – queuing for retry", response.Error?.Code);
            await _queue.EnqueueAsync("UnregisterGroup", url, "POST", payload, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Registers an officer/position holder as an attendee of an intergroup meeting.
    /// Enqueues the call if the device is offline or the request fails.
    /// </summary>
    public async Task<ApiResponse<IntergroupMeetingOfficerRegistration>> RegisterOfficerAsync(
        int intergroupMeetingId,
        int officerId,
        string positionName,
        string officerName,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline())
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/register-officer");
            var payload = Serialize(new
            {
                officer_id = officerId,
                position_name = positionName,
                officer_name = officerName
            });

            Logger.Information("Device offline – queuing RegisterOfficer for meeting {Id}", intergroupMeetingId);
            await _queue.EnqueueAsync("RegisterOfficer", url, "POST", payload, cancellationToken);
            return OfflineQueued<IntergroupMeetingOfficerRegistration>();
        }

        var client = await GetClientAsync();
        var response = await client.RegisterOfficerAsync(
            intergroupMeetingId, officerId, positionName, officerName, cancellationToken);

        if (!response.Success && IsTransientError(response))
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/register-officer");
            var payload = Serialize(new
            {
                officer_id = officerId,
                position_name = positionName,
                officer_name = officerName
            });

            Logger.Warning("RegisterOfficer failed ({Code}) – queuing for retry", response.Error?.Code);
            await _queue.EnqueueAsync("RegisterOfficer", url, "POST", payload, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Unregisters an officer from an intergroup meeting.
    /// Enqueues the call if the device is offline or the request fails.
    /// </summary>
    public async Task<ApiResponse<IntergroupMeetingOfficerRegistration>> UnregisterOfficerAsync(
        int intergroupMeetingId,
        int officerId,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline())
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/unregister-officer");
            var payload = Serialize(new { officer_id = officerId });

            Logger.Information("Device offline – queuing UnregisterOfficer for meeting {Id}", intergroupMeetingId);
            await _queue.EnqueueAsync("UnregisterOfficer", url, "POST", payload, cancellationToken);
            return OfflineQueued<IntergroupMeetingOfficerRegistration>();
        }

        var client = await GetClientAsync();
        var response = await client.UnregisterOfficerAsync(intergroupMeetingId, officerId, cancellationToken);

        if (!response.Success && IsTransientError(response))
        {
            var url = BuildRelativePath($"intergroup-meetings/{intergroupMeetingId}/unregister-officer");
            var payload = Serialize(new { officer_id = officerId });

            Logger.Warning("UnregisterOfficer failed ({Code}) – queuing for retry", response.Error?.Code);
            await _queue.EnqueueAsync("UnregisterOfficer", url, "POST", payload, cancellationToken);
        }

        return response;
    }

    /// <summary>
    /// Updates a member (GSR / position holder details).
    /// Enqueues the call if the device is offline or the request fails.
    /// </summary>
    public async Task<ApiResponse<Member>> UpdateMemberAsync(
        int memberId,
        UpdateMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsOnline())
        {
            var url = BuildRelativePath($"members/{memberId}/update");
            var payload = Serialize(request);

            Logger.Information("Device offline – queuing UpdateMember for member {Id}", memberId);
            await _queue.EnqueueAsync("UpdateMember", url, "POST", payload, cancellationToken);
            return OfflineQueued<Member>();
        }

        var client = await GetClientAsync();
        var response = await client.UpdateMemberAsync(memberId, request, cancellationToken);

        if (!response.Success && IsTransientError(response))
        {
            var url = BuildRelativePath($"members/{memberId}/update");
            var payload = Serialize(request);

            Logger.Warning("UpdateMember failed ({Code}) – queuing for retry", response.Error?.Code);
            await _queue.EnqueueAsync("UpdateMember", url, "POST", payload, cancellationToken);
        }

        return response;
    }

    // --------------------------------------------------------------- private helpers

    private static bool IsOnline()
    {
        try { return Connectivity.Current.NetworkAccess == NetworkAccess.Internet; }
        catch { return false; }
    }

    /// <summary>
    /// Returns true for errors that are worth retrying (network issues, server errors, rate limits).
    /// Returns false for permanent client errors (400 Bad Request, 401 Unauthorized, 403 Forbidden, 404 Not Found).
    /// </summary>
    private static bool IsTransientError<T>(ApiResponse<T> response) where T : class
    {
        // network_error = connectivity problem → definitely queue
        if (response.Error?.Code == "network_error") return true;

        // 5xx → server error, retry
        if (response.StatusCode >= 500) return true;

        // 429 Too Many Requests, 408 Request Timeout → retry
        if (response.StatusCode is 429 or 408) return true;

        // Anything else (4xx, parse errors) — do not queue, let caller handle
        return false;
    }

    private async Task<UnityRestSharp> GetClientAsync()
    {
        var config = await _configService.LoadUnityConfigurationAsync();
        if (!config.IsValid())
            throw new InvalidOperationException(
                "Unity API is not configured. Please set the Base URL and API Key in Settings.");

        // Re-create the client only if the base URL changed (e.g. user updated settings)
        if (_innerClient is null || _cachedBaseUrl != config.BaseUrl)
        {
            _innerClient?.Dispose();
            _innerClient = new UnityRestSharp(config.BaseUrl, config.ApiKey);
            _cachedBaseUrl = config.BaseUrl;
        }

        return _innerClient;
    }

    private static string BuildRelativePath(string relativeEndpoint)
    {
        // Store only the relative API path in the queue. The full URL is resolved
        // from current config at flush time by ApiQueueService, preventing stale
        // base URLs if the user changes Unity settings between enqueue and flush.
        return $"wp-json/integrity/v1/{relativeEndpoint}";
    }

    private static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    private static ApiResponse<T> OfflineQueued<T>() where T : class =>
        new()
        {
            Success = false,
            StatusCode = 0,
            Error = new ApiError
            {
                Code = "queued_offline",
                Message = "Request has been queued and will be sent when connectivity is restored."
            }
        };

    // --------------------------------------------------------------- IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _innerClient?.Dispose();
            _disposed = true;
        }
    }
}