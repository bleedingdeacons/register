using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class AttendanceService : IAttendanceRegistration<Position>, IAttendanceRegistration<Meeting>, IDisposable
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AttendanceService>();

        private readonly IMailService _mailService;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly IMeetingRepository _meetingRepository;
        private readonly IPositionRepository _positionRepository;
        private readonly QueueingUnityApiService _unityApiService;
        private readonly IConfigurationService _configService;

        private readonly EventHandler<EmailSentEventArgs> _emailSentHandler;
        private readonly EventHandler<EmailFailedEventArgs> _emailFailedHandler;
        private bool _disposed;

        public AttendanceService(
            IMeetingRepository meetingRepository,
            IPositionRepository positionRepository,
            IEmailTemplateService emailTemplate,
            IMailService mailService,
            QueueingUnityApiService unityApiService,
            IConfigurationService configService)
        {
            _positionRepository = positionRepository;
            _meetingRepository = meetingRepository;
            _mailService = mailService;
            _emailTemplate = emailTemplate;
            _unityApiService = unityApiService;
            _configService = configService;

            _emailSentHandler = (s, e) => Logger.Information("Email sent to {Recipient}", e.Email.To);
            _emailFailedHandler = (s, e) => Logger.Warning("Email failed for {Recipient}: {Error}", e.Email.To, e.Error);

            _mailService.EmailSent += _emailSentHandler;
            _mailService.EmailFailed += _emailFailedHandler;
        }

        public async Task Register(Position entity)
        {
            entity.Attended = true;
            await _positionRepository.SavePositionAsync(entity);

            // Notify the Unity API that this position holder has registered attendance
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                var officerName = entity.MemberAnonymousName ?? string.Empty;
                var positionName = entity.PositionName ?? entity.PositionLongName ?? string.Empty;

                var response = await _unityApiService.RegisterOfficerAsync(
                    intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                    officerId: entity.ID,
                    positionName: positionName,
                    officerName: officerName);

                if (response.Success)
                    Logger.Information("Position {PositionName} attendance registered with Unity API", positionName);
                else if (response.Error?.Code == "queued_offline")
                    Logger.Information("Position {PositionName} Unity API registration queued (offline)", positionName);
                else
                    Logger.Warning("Position {PositionName} Unity API registration returned: {Error}", positionName, response.Error?.Message);
            }
            else
            {
                Logger.Information(
                    "Position {PositionName} attendance registered locally (Unity API not configured or no active meeting set)",
                    entity.PositionName);
            }
        }

        public async Task Register(Meeting entity)
        {
            entity.Attended = true;
            await _meetingRepository.SaveMeetingAsync(entity);

            // Notify the Unity API that this group/GSR has registered attendance
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                // When standing in, the proxy is the attending GSR; otherwise use the primary GSR
                var isProxy = entity.ProxyAttendance == true;
                var registeredGsrName = isProxy
                    ? entity.ProxyName ?? string.Empty
                    : entity.Group?.Gsrs.FirstOrDefault()?.Name ?? string.Empty;

                // Unity register-group requires the member ID of the GSR on record.
                // A proxy attends on behalf of the same group slot.
                var memberId = entity.Group?.Gsrs.FirstOrDefault()?.ID ?? 0;

                if (memberId > 0)
                {
                    var meetingGroup = entity.Group?.Name ?? entity.Name ?? string.Empty;

                    var response = await _unityApiService.RegisterAttendeeAsync(
                        intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                        memberId: memberId,
                        meetingGroup: meetingGroup,
                        gsrName: registeredGsrName,
                        gsrProxy: isProxy,
                        gsrProxyName: isProxy ? entity.ProxyName : null);

                    if (response.Success)
                        Logger.Information("Meeting {MeetingName} group attendance registered with Unity API", entity.Name);
                    else if (response.Error?.Code == "queued_offline")
                        Logger.Information("Meeting {MeetingName} Unity API registration queued (offline)", entity.Name);
                    else
                        Logger.Warning("Meeting {MeetingName} Unity API registration returned: {Error}", entity.Name, response.Error?.Message);
                }
                else
                {
                    Logger.Warning(
                        "Meeting {MeetingName} has no GSR with a Unity member ID — skipping API registration",
                        entity.Name);
                }
            }
            else
            {
                Logger.Information(
                    "Meeting {MeetingName} attendance registered locally (Unity API not configured or no active meeting set)",
                    entity.Name);
            }

            // TODO: Enable welcome email sending once SMTP is configured
            // var welcome = new WelcomeEmail
            // {
            //     FirstName = entity.GetFirstName(),
            //     MeetingName = entity.Name,
            //     StartTime = entity.Time,
            //     Location = entity.Location,
            //     Address = entity.Address,
            //     MeetingContacts = entity.GetContacts()
            // };
            // var emailBody = await _emailTemplate.RenderTemplateAsync("WelcomeEmail", welcome);
            // foreach (var gsr in entity.Group?.Gsrs ?? [])
            //     if (!string.IsNullOrEmpty(gsr.EmailPersonal))
            //         await _mailService.SendEmailAsync(gsr.EmailPersonal, "Important information about your meeting.", emailBody, isHtml: true);

            Logger.Information("Meeting {MeetingName} attendance registered", entity.Name);
        }

        public async Task Unregister(Position entity)
        {
            entity.Attended = false;
            await _positionRepository.SavePositionAsync(entity);
            Logger.Information("Position {PositionName} attendance unregistered", entity.PositionName);
        }

        public async Task Unregister(Meeting entity)
        {
            entity.Attended = false;
            entity.ProxyAttendance = false;
            entity.ProxyEmail = null;
            entity.ProxyName = null;
            await _meetingRepository.SaveMeetingAsync(entity);

            // Notify the Unity API that this group has unregistered
            var config = await _configService.LoadUnityConfigurationAsync();
            if (config.ActiveIntergroupMeetingId.HasValue && config.IsValid())
            {
                var memberId = entity.Group?.Gsrs.FirstOrDefault()?.ID ?? 0;

                if (memberId > 0)
                {
                    var response = await _unityApiService.UnregisterAttendeeAsync(
                        intergroupMeetingId: config.ActiveIntergroupMeetingId.Value,
                        memberId: memberId);

                    if (response.Success)
                        Logger.Information("Meeting {MeetingName} group attendance unregistered with Unity API", entity.Name);
                    else if (response.Error?.Code == "queued_offline")
                        Logger.Information("Meeting {MeetingName} Unity API unregistration queued (offline)", entity.Name);
                    else
                        Logger.Warning("Meeting {MeetingName} Unity API unregistration returned: {Error}", entity.Name, response.Error?.Message);
                }
                else
                {
                    Logger.Warning(
                        "Meeting {MeetingName} has no GSR with a Unity member ID — skipping API unregistration",
                        entity.Name);
                }
            }
            else
            {
                Logger.Information(
                    "Meeting {MeetingName} attendance unregistered locally (Unity API not configured or no active meeting set)",
                    entity.Name);
            }

            Logger.Information("Meeting {MeetingName} attendance unregistered", entity.Name);
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