using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	public interface IConfigurationService
	{
		SmtpConfiguration GetSmtpConfiguration();
		void UpdateSmtpConfiguration(SmtpConfiguration config);
		Task SaveSmtpConfigurationAsync(SmtpConfiguration config);
		Task<SmtpConfiguration> LoadSmtpConfigurationAsync();

		Task SaveUnityConfigurationAsync(UnityConfiguration config);
		Task<UnityConfiguration> LoadUnityConfigurationAsync();

		/// <summary>Persists only the active intergroup meeting ID, leaving all other settings untouched.</summary>
		Task SaveActiveIntergroupMeetingAsync(int? meetingId);

		BetterStackConfiguration GetBetterStackConfiguration();
		Task SaveBetterStackConfigurationAsync(BetterStackConfiguration config);
		Task<BetterStackConfiguration> LoadBetterStackConfigurationAsync();
	}
}