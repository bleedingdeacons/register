using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	public interface IPhoneNumberService
	{
		PhoneValidationResult Validate(string input, string regionCode = "GB");
		string? FormatNational(string input, string regionCode = "GB");
		string? FormatInternational(string input, string regionCode = "GB");
		string? FormatE164(string input, string regionCode = "GB");
		PhoneNumberKind GetNumberKind(string input, string regionCode = "GB");
	}

	public record PhoneValidationResult(
	bool IsValid,
	string? E164,
	string? National,
	string? International,
	PhoneNumberKind Kind,
	string? ErrorMessage);

	public enum PhoneNumberKind
	{
		Unknown,
		FixedLine,
		Mobile,
		FixedLineOrMobile,
		TollFree,
		PremiumRate,
		SharedCost,
		Voip,
		PersonalNumber,
		Pager,
		Uan,
		Voicemail
	}
}
