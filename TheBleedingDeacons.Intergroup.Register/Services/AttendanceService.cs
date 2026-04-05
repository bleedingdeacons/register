using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Manages attendance registration state locally.
	///
	/// All changes are written to the local <see cref="UnityDbContext"/> only.
	/// The <see cref="ReconciliationService"/> is responsible for detecting
	/// these changes (via snapshot diffing) and pushing them to the Unity API
	/// in the correct dependency order at reconciliation time.
	/// </summary>
	public class AttendanceService : IAttendanceRegistration<Position>, IAttendanceRegistration<Group>, IDisposable
	{
		private static readonly ILogger Logger = AppLogger.ForContext<AttendanceService>();

		private readonly IMailService _mailService;
		private readonly IEmailTemplateService _emailTemplate;
		private readonly UnityDbContext _dbContext;

		private readonly EventHandler<EmailSentEventArgs> _emailSentHandler;
		private readonly EventHandler<EmailFailedEventArgs> _emailFailedHandler;
		private bool _disposed;

		public AttendanceService(
			IEmailTemplateService emailTemplate,
			IMailService mailService,
			UnityDbContext dbContext)
		{
			_mailService = mailService;
			_emailTemplate = emailTemplate;
			_dbContext = dbContext;

			_emailSentHandler = (s, e) => Logger.Information("Email sent to {Recipient}", e.Email.To);
			_emailFailedHandler = (s, e) => Logger.Warning("Email failed for {Recipient}: {Error}", e.Email.To, e.Error);

			_mailService.EmailSent += _emailSentHandler;
			_mailService.EmailFailed += _emailFailedHandler;
		}

		public async Task Register(Position entity)
		{
			Logger.Information("Position {PositionName} attendance registered locally", entity.ShortDescription);
			await SetPositionRegisteredAsync(entity.Id, true);
		}

		public async Task Register(Group entity)
		{
			Logger.Information("Group {GroupName} attendance registered locally", entity.Name);
			await SetGroupRegisteredAsync(entity.Id, true);
		}

		public async Task Unregister(Position entity)
		{
			Logger.Information("Position {PositionName} attendance unregistered locally", entity.ShortDescription);
			await SetPositionRegisteredAsync(entity.Id, false);
		}

		public async Task Unregister(Group entity)
		{
			Logger.Information("Group {GroupName} attendance unregistered locally", entity.Name);
			await SetGroupRegisteredAsync(entity.Id, false);
		}

		private async Task SetGroupRegisteredAsync(int groupId, bool registered, CancellationToken ct = default)
		{
			try
			{
				var group = await _dbContext.Groups.FindAsync(new object[] { groupId }, ct);
				if (group != null)
				{
					group.Registered = registered;
					await _dbContext.SaveChangesAsync(ct);
				}
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "Failed to persist Registered state for group {GroupId}", groupId);
			}
		}

		private async Task SetPositionRegisteredAsync(int positionId, bool registered, CancellationToken ct = default)
		{
			try
			{
				var position = await _dbContext.Positions.FindAsync(new object[] { positionId }, ct);
				if (position != null)
				{
					position.Registered = registered;
					await _dbContext.SaveChangesAsync(ct);
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