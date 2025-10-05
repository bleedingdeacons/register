using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    public class AttendanceService : IAttendanceRegistration<Position>, IAttendanceRegistration<Group>
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AttendanceService>();

        private readonly IMailService _mailService;
        private readonly IEmailTemplateService _emailTemplate;
        private readonly IGroupRepository _groupRepository;
        private readonly IPositionRepository _positionRepository;

        public AttendanceService(IGroupRepository groupRepository,  IPositionRepository positionRepository, IEmailTemplateService emailTemplate, IMailService mailService)
        {
            _positionRepository = positionRepository;
            _groupRepository = groupRepository;
            _mailService = mailService;
            _emailTemplate = emailTemplate;

            _mailService.EmailSent += (s, e) => Console.WriteLine($"Email sent to {e.Email.To}");
            _mailService.EmailFailed += (s, e) => Console.WriteLine($"Email failed: {e.Error}");
        }

        public async Task Register(Position entity)
        {
            entity.Attended = true;
            await _positionRepository.SavePositionAsync(entity);
        }

        public async Task Register(Group entity)
        {

            entity.Attended = true;
            await _groupRepository.SaveGroupAsync(entity);

            var welcome = new WelcomeEmail
            {
                FirstName = entity.GetGsrFirstName(),
                GroupName = entity.Name,
                StartTime = entity.Time,
                Location = entity.Location,
                Address = entity.Address,
                //Email = entity.GsrEmailPersonal,
                //Mobile = entity.GsrPhone,                
                GroupContacts = entity.GetContacts()
            };

            var emailBody = await _emailTemplate.RenderTemplateAsync("WelcomeEmail", welcome);

            if (entity.GsrEmailPersonal != null)
                await _mailService.SendEmailAsync(entity.GsrEmailPersonal, "Important information about your group.", emailBody, isHtml:true);

        }

        public async Task Unregister(Position entity)
        {
            throw new NotImplementedException();
        }

        public async Task Unregister(Group entity)
        {
            throw new NotImplementedException();
        }


    }
}
