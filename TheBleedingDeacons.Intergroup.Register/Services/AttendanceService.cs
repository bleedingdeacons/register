using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class AttendanceService : IAttendanceRegistration<Position>, IAttendanceRegistration<Group>, IDisposable
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AttendanceService>();

        private readonly IMailService _mailService;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly QueueingUnityApiService _unityApiService;
        private readonly IConfigurationService _configService;
        private readonly UnityDbContext _dbContext;

        private readonly EventHandler<EmailSentEventArgs> _emailSentHandler;
        private readonly EventHandler<EmailFailedEventArgs> _emailFailedHandler;
        private bool _disposed;

        public AttendanceService(
            IEmailTemplateService emailTemplate,
            IMailService mailService,
            QueueingUnityApiService unityApiService,
            IConfigurationService configService,
            UnityDbContext dbContext)
        {
            _mailService = mailService;
            _emailTemplate = emailTemplate;
            _unityApiService = unityApiService;
            _configService = configService;
            _dbContext = dbContext;

            _emailSentHandler = (s, e) => Logger.Information("Email sent to {Recipient}", e.Email.To);
            _emailFailedHandler = (s, e) => Logger.Warning("Email failed for {Recipient}: {Error}", e.Email.To, e.Error);

            _mailService.EmailSent += _emailSentHandler;
            _mailService.EmailFailed += _emailFailedHandler;
        }

        public async Task Register(Position entity)
        {
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                var holder = entity.Holders.FirstOrDefault();
                if (holder == null || holder.IsTemporary)
                {
                    Logger.Warning("Position {PositionName} has no associated member — skipping Unity API registration",
                        entity.ShortDescription);
                }
                else
                {
                    var officerName = holder.AnonymousName;
                    var positionName = entity.ShortDescription ?? entity.LongName ?? string.Empty;

                    var response = await _unityApiService.RegisterOfficerAsync(
                        intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                        officerId: holder.Id,
                        positionName: positionName,
                        officerName: officerName);

                    if (response.Success)
                        Logger.Information("Position {PositionName} attendance registered with Unity API", positionName);
                    else if (response.Error?.Code == "queued_offline")
                        Logger.Information("Position {PositionName} Unity API registration queued (offline)", positionName);
                    else
                        Logger.Warning("Position {PositionName} Unity API registration returned: {Error}", positionName, response.Error?.Message);
                }
            }
            else
            {
                Logger.Information(
                    "Position {PositionName} attendance registered locally (Unity API not configured or no active meeting set)",
                    entity.ShortDescription);
            }

            // Persist the registered state
            await SetPositionRegisteredAsync(entity.Id, true);
        }

        public async Task Register(Group entity)
        {
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                var isProxy = false; // TODO: proxy info needs to come from ViewModel context
                var gsr = entity.Members.FirstOrDefault(m => m.IsGsr);
                var gsrName = gsr?.AnonymousName ?? string.Empty;
                var groupId = entity.Id;
                var memberId = gsr?.Id ?? 0;

                if (groupId > 0)
                {
                    var response = await _unityApiService.RegisterGroupAsync(
                        intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                        groupId: groupId,
                        memberId: memberId,
                        gsrName: gsrName,
                        gsrProxy: isProxy,
                        gsrProxyName: null);

                    if (response.Success)
                        Logger.Information("Group {GroupName} attendance registered with Unity API", entity.Name);
                    else if (response.Error?.Code == "queued_offline")
                        Logger.Information("Group {GroupName} Unity API registration queued (offline)", entity.Name);
                    else
                        Logger.Warning("Group {GroupName} Unity API registration returned: {Error}", entity.Name, response.Error?.Message);
                }
                else
                {
                    Logger.Warning("Group {GroupName} has no group ID — skipping API registration", entity.Name);
                }
            }
            else
            {
                Logger.Information(
                    "Group {GroupName} attendance registered locally (Unity API not configured or no active meeting set)",
                    entity.Name);
            }

            Logger.Information("Group {GroupName} attendance registered", entity.Name);

            // Persist the registered state
            await SetGroupRegisteredAsync(entity.Id, true);
        }

        public async Task Unregister(Position entity)
        {
            Logger.Information("Position {PositionName} attendance unregistered", entity.ShortDescription);

            // Persist the unregistered state
            await SetPositionRegisteredAsync(entity.Id, false);
        }

        public async Task Unregister(Group entity)
        {
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                var groupId = entity.Id;

                if (groupId > 0)
                {
                    var response = await _unityApiService.UnregisterGroupAsync(
                        intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                        groupId: groupId);

                    if (response.Success)
                        Logger.Information("Group {GroupName} attendance unregistered with Unity API", entity.Name);
                    else if (response.Error?.Code == "queued_offline")
                        Logger.Information("Group {GroupName} Unity API unregistration queued (offline)", entity.Name);
                    else
                        Logger.Warning("Group {GroupName} Unity API unregistration returned: {Error}", entity.Name, response.Error?.Message);
                }
            }

            Logger.Information("Group {GroupName} attendance unregistered", entity.Name);

            // Persist the unregistered state
            await SetGroupRegisteredAsync(entity.Id, false);
        }

        private async Task SetGroupRegisteredAsync(int groupId, bool registered)
        {
            try
            {
                var group = await _dbContext.Groups.FindAsync(groupId);
                if (group != null)
                {
                    group.Registered = registered;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to persist Registered state for group {GroupId}", groupId);
            }
        }

        private async Task SetPositionRegisteredAsync(int positionId, bool registered)
        {
            try
            {
                var position = await _dbContext.Positions.FindAsync(positionId);
                if (position != null)
                {
                    position.Registered = registered;
                    await _dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to persist Registered state for position {PositionId}", positionId);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _mailService.EmailSent -= _emailSentHandler;
                _mailService.EmailFailed -= _emailFailedHandler;
                _disposed = true;
            }
        }
    }
}