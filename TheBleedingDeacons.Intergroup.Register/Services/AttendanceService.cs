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

        private readonly EventHandler<EmailSentEventArgs> _emailSentHandler;
        private readonly EventHandler<EmailFailedEventArgs> _emailFailedHandler;
        private bool _disposed;

        public AttendanceService(IMeetingRepository meetingRepository, IPositionRepository positionRepository, IEmailTemplateService emailTemplate, IMailService mailService)
        {
            _positionRepository = positionRepository;
            _meetingRepository = meetingRepository;
            _mailService = mailService;
            _emailTemplate = emailTemplate;

            _emailSentHandler = (s, e) => Logger.Information("Email sent to {Recipient}", e.Email.To);
            _emailFailedHandler = (s, e) => Logger.Warning("Email failed for {Recipient}: {Error}", e.Email.To, e.Error);

            _mailService.EmailSent += _emailSentHandler;
            _mailService.EmailFailed += _emailFailedHandler;
        }

        public async Task Register(Position entity)
        {
            entity.Attended = true;
            await _positionRepository.SavePositionAsync(entity);
        }

        public async Task Register(Meeting entity)
        {
            entity.Attended = true;
            await _meetingRepository.SaveMeetingAsync(entity);

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
            // if (!string.IsNullOrEmpty(entity.Group?.Gsr?.EmailPersonal))
            //     await _mailService.SendEmailAsync(entity.Group?.Gsr?.EmailPersonal, "Important information about your meeting.", emailBody, isHtml: true);

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
