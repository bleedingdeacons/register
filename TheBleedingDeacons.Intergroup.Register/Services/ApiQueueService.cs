using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Outbox-pattern service that persists failed or offline Unity API calls to SQLite
/// and replays them when the device regains connectivity.
///
/// Lifecycle
/// ---------
/// • Call <see cref="StartAsync"/> once from <c>MauiProgram</c> (or App.xaml.cs) after the
///   DI container is built. The service subscribes to <see cref="Connectivity.ConnectivityChanged"/>
///   and automatically flushes on every transition to an online state.
/// • Individual callers just <c>await EnqueueAsync(…)</c> — they do not need to know whether
///   the device is online.
/// </summary>
public class ApiQueueService : IApiQueueService, IDisposable
{
    
    private const int MaxAttempts = 10;
    private static readonly ILogger Logger = AppLogger.ForContext<ApiQueueService>();

    private readonly IDbContextFactory<RegisterContext> _dbFactory;
    private readonly IConfigurationService _configService;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private bool _disposed;

    // ------------------------------------------------------------------ ctor / lifecycle
    private const string OfflineModePreferenceKey = "api_queue_offline_mode";

    public bool IsOfflineModeEnabled
    {
        get => Preferences.Default.Get(OfflineModePreferenceKey, false);
        set
        {
            if (Preferences.Default.Get(OfflineModePreferenceKey, false) == value) return;
            Preferences.Default.Set(OfflineModePreferenceKey, value);
            if (!value) _ = FlushAsync();
        }
    }

    public ApiQueueService(
        IDbContextFactory<RegisterContext> dbFactory,
        IConfigurationService configService)
    {
        _dbFactory = dbFactory;
        _configService = configService;
    }

    /// <summary>
    /// Subscribes to connectivity changes and does an initial flush.
    /// Call this once during app startup.
    /// </summary>
    public void Start()
    {
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        Logger.Information("ApiQueueService started – connectivity monitoring active");

        
        _ = FlushAsync();
    }

   
    /// <inheritdoc />
    public event EventHandler<int>? PendingCountChanged;

    /// <inheritdoc />
    public async Task EnqueueAsync(
        string operationType,
        string url,
        string httpMethod,
        string? jsonPayload = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var entry = new QueuedApiCall
        {
            OperationType = operationType,
            Url = url,
            HttpMethod = httpMethod.ToUpperInvariant(),
            JsonPayload = jsonPayload,
            CreatedUtc = DateTime.UtcNow
        };

        db.QueuedApiCalls.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        Logger.Information(
            "Enqueued API call {OperationType} → {Url} (queue entry {Id})",
            operationType, url, entry.Id);

        var pending = await GetPendingCountAsync(cancellationToken);
        RaisePendingCountChanged(pending);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Only one flush at a time
        if (!await _flushLock.WaitAsync(0, cancellationToken))
        {
            Logger.Debug("FlushAsync skipped – another flush is already running");
            return;
        }

        try
        {
            if (!IsOnline())
            {
                Logger.Debug("FlushAsync skipped – device is offline");
                return;
            }

            var config = await _configService.LoadUnityConfigurationAsync();
            if (!config.IsValid())
            {
                Logger.Warning("FlushAsync skipped – Unity API not configured");
                return;
            }

            using var http = CreateHttpClient(config.ApiKey);

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var pending = await db.QueuedApiCalls
                .Where(q => !q.IsFailed && q.AttemptCount < MaxAttempts)
                .OrderBy(q => q.CreatedUtc)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                Logger.Debug("FlushAsync – queue is empty");
                return;
            }

            Logger.Information("FlushAsync – processing {Count} queued call(s)", pending.Count);

            int succeeded = 0, failed = 0;

            foreach (var entry in pending)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!IsOnline()) break; // stop early if we lose connectivity mid-flush

                entry.AttemptCount++;
                entry.LastAttemptUtc = DateTime.UtcNow;

                try
                {
                    using var response = await SendAsync(http, entry, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Information(
                            "Queued call {Id} ({OperationType}) succeeded on attempt {Attempt}",
                            entry.Id, entry.OperationType, entry.AttemptCount);

                        db.QueuedApiCalls.Remove(entry);
                        succeeded++;
                    }
                    else
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken);
                        entry.LastError = $"HTTP {(int)response.StatusCode}: {body.Truncate(200)}";

                        // Permanent failures (4xx except 429 Too Many Requests) — give up
                        if ((int)response.StatusCode is >= 400 and < 500
                            and not 429
                            and not 408)
                        {
                            entry.IsFailed = true;
                            Logger.Warning(
                                "Queued call {Id} ({OperationType}) permanently failed: {Error}",
                                entry.Id, entry.OperationType, entry.LastError);
                        }
                        else
                        {
                            Logger.Warning(
                                "Queued call {Id} ({OperationType}) failed (attempt {Attempt}/{Max}): {Error}",
                                entry.Id, entry.OperationType, entry.AttemptCount, MaxAttempts, entry.LastError);
                        }

                        failed++;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    entry.LastError = ex.Message.Truncate(200);

                    Logger.Warning(
                        ex,
                        "Queued call {Id} ({OperationType}) network error on attempt {Attempt}",
                        entry.Id, entry.OperationType, entry.AttemptCount);

                    failed++;

                    // Network error – no point trying the rest right now
                    break;
                }

                if (entry.AttemptCount >= MaxAttempts && !entry.IsFailed)
                {
                    entry.IsFailed = true;
                    Logger.Error(
                        "Queued call {Id} ({OperationType}) abandoned after {Max} attempts",
                        entry.Id, entry.OperationType, MaxAttempts);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            Logger.Information(
                "FlushAsync complete – {Succeeded} succeeded, {Failed} failed/deferred",
                succeeded, failed);

            var remaining = await GetPendingCountAsync(cancellationToken);
            RaisePendingCountChanged(remaining);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.QueuedApiCalls
            .CountAsync(q => !q.IsFailed, cancellationToken);
    }

    // ------------------------------------------------------------------ private helpers

    private static bool IsOnline()
    {
        try
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }
        catch
        {
            return false;
        }
    }

    private HttpClient CreateHttpClient(string apiKey)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntegrityClient/1.0");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        QueuedApiCall entry,
        CancellationToken cancellationToken)
    {
        return entry.HttpMethod switch
        {
            "POST" => await http.PostAsync(
                entry.Url,
                entry.JsonPayload is null
                    ? null
                    : new StringContent(entry.JsonPayload, Encoding.UTF8, "application/json"),
                cancellationToken),

            "PUT" => await http.PutAsync(
                entry.Url,
                entry.JsonPayload is null
                    ? null
                    : new StringContent(entry.JsonPayload, Encoding.UTF8, "application/json"),
                cancellationToken),

            "PATCH" => await http.PatchAsync(
                entry.Url,
                entry.JsonPayload is null
                    ? null
                    : new StringContent(entry.JsonPayload, Encoding.UTF8, "application/json"),
                cancellationToken),

            "DELETE" => await http.DeleteAsync(entry.Url, cancellationToken),

            _ => throw new InvalidOperationException($"Unsupported HTTP method: {entry.HttpMethod}")
        };
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            Logger.Information("Connectivity restored – triggering queue flush");
            _ = FlushAsync();
        }
    }

    private void RaisePendingCountChanged(int count)
    {
        try { PendingCountChanged?.Invoke(this, count); }
        catch (Exception ex) { Logger.Warning(ex, "PendingCountChanged handler threw"); }
    }

    // ------------------------------------------------------------------ IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            _flushLock.Dispose();
            _disposed = true;
        }
    }
}

internal static class StringExtensions
{
    internal static string Truncate(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}