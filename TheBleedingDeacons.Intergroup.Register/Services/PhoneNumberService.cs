using PhoneNumbers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	public class PhoneNumberService : IPhoneNumberService
	{
		private readonly PhoneNumberUtil _util = PhoneNumberUtil.GetInstance();

		public PhoneValidationResult Validate(string input, string regionCode = "GB")
		{
			if (string.IsNullOrWhiteSpace(input))
				return new(false, null, null, null, PhoneNumberKind.Unknown, "Number is empty.");

			try
			{
				var parsed = _util.Parse(input, regionCode);

				if (!_util.IsValidNumberForRegion(parsed, regionCode))
					return new(false, null, null, null, PhoneNumberKind.Unknown,
						$"Not a valid number for region {regionCode}.");

				return new(
					IsValid: true,
					E164: _util.Format(parsed, PhoneNumberFormat.E164),
					National: _util.Format(parsed, PhoneNumberFormat.NATIONAL),
					International: _util.Format(parsed, PhoneNumberFormat.INTERNATIONAL),
					Kind: MapType(_util.GetNumberType(parsed)),
					ErrorMessage: null);
			}
			catch (NumberParseException ex)
			{
				return new(false, null, null, null, PhoneNumberKind.Unknown, ex.Message);
			}
		}

		public string? FormatNational(string input, string regionCode = "GB") =>
			TryFormat(input, regionCode, PhoneNumberFormat.NATIONAL);

		public string? FormatInternational(string input, string regionCode = "GB") =>
			TryFormat(input, regionCode, PhoneNumberFormat.INTERNATIONAL);

		public string? FormatE164(string input, string regionCode = "GB") =>
			TryFormat(input, regionCode, PhoneNumberFormat.E164);

		public PhoneNumberKind GetNumberKind(string input, string regionCode = "GB")
		{
			try
			{
				var parsed = _util.Parse(input, regionCode);
				return MapType(_util.GetNumberType(parsed));
			}
			catch (NumberParseException)
			{
				return PhoneNumberKind.Unknown;
			}
		}

		private string? TryFormat(string input, string regionCode, PhoneNumberFormat format)
		{
			try
			{
				var parsed = _util.Parse(input, regionCode);
				return _util.IsValidNumber(parsed) ? _util.Format(parsed, format) : null;
			}
			catch (NumberParseException)
			{
				return null;
			}
		}

		private static PhoneNumberKind MapType(PhoneNumberType type) => type switch
		{
			PhoneNumberType.FIXED_LINE => PhoneNumberKind.FixedLine,
			PhoneNumberType.MOBILE => PhoneNumberKind.Mobile,
			PhoneNumberType.FIXED_LINE_OR_MOBILE => PhoneNumberKind.FixedLineOrMobile,
			PhoneNumberType.TOLL_FREE => PhoneNumberKind.TollFree,
			PhoneNumberType.PREMIUM_RATE => PhoneNumberKind.PremiumRate,
			PhoneNumberType.SHARED_COST => PhoneNumberKind.SharedCost,
			PhoneNumberType.VOIP => PhoneNumberKind.Voip,
			PhoneNumberType.PERSONAL_NUMBER => PhoneNumberKind.PersonalNumber,
			PhoneNumberType.PAGER => PhoneNumberKind.Pager,
			PhoneNumberType.UAN => PhoneNumberKind.Uan,
			PhoneNumberType.VOICEMAIL => PhoneNumberKind.Voicemail,
			_ => PhoneNumberKind.Unknown
		};
	}
}
