using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Wraps libphonenumber for the GSR and officer edit screens. The behaviour
/// that matters is the boundary: what counts as valid for a region, and that a
/// bad number comes back as a result object rather than an exception thrown
/// into the edit flow.
/// </summary>
public class PhoneNumberServiceTests
{
	private readonly PhoneNumberService _service = new();

	// The landline is from Ofcom's reserved drama range (0117 496 0xxx), which
	// libphonenumber accepts. The mobile is NOT from the drama range: see
	// Validate_RejectsOfcomsReservedDramaMobileRange for why it cannot be.
	private const string UkMobile = "07912 345678";
	private const string UkLandline = "0117 496 0123";

	[Theory]
	[InlineData(UkMobile)]
	[InlineData(UkLandline)]
	[InlineData("+44 7912 345678")]
	public void Validate_AcceptsValidGbNumbers(string input)
	{
		var result = _service.Validate(input);

		Assert.True(result.IsValid);
		Assert.Null(result.ErrorMessage);
	}

	[Fact]
	public void Validate_ReturnsAllThreeFormats()
	{
		var result = _service.Validate(UkMobile);

		Assert.Equal("+447912345678", result.E164);
		Assert.Equal("07912 345678", result.National);
		Assert.Equal("+44 7912 345678", result.International);
	}

	[Theory]
	[InlineData("07700 900123")]
	[InlineData("+44 7700 900123")]
	public void Validate_RejectsOfcomsReservedDramaMobileRange(string input)
	{
		// Worth knowing before someone loses an afternoon to it: 07700 900xxx
		// is the range Ofcom reserves for film and TV, and libphonenumber's GB
		// metadata does not consider it a valid mobile. Anyone smoke-testing
		// the app with a "safe" fake mobile will be told their number is
		// invalid. The drama LANDLINE ranges (e.g. 0117 496 0xxx) are accepted,
		// which makes the asymmetry especially easy to trip over.
		var result = _service.Validate(input);

		Assert.False(result.IsValid);
		Assert.Contains("GB", result.ErrorMessage!, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Validate_ReportsEmptyInputWithoutThrowing(string? input)
	{
		var result = _service.Validate(input!);

		Assert.False(result.IsValid);
		Assert.Equal("Number is empty.", result.ErrorMessage);
		Assert.Equal(PhoneNumberKind.Unknown, result.Kind);
	}

	[Fact]
	public void Validate_ReportsAnUnparseableNumberAsAMessageNotAnException()
	{
		var result = _service.Validate("banana");

		Assert.False(result.IsValid);
		Assert.NotNull(result.ErrorMessage);
		Assert.Null(result.E164);
	}

	[Fact]
	public void Validate_RejectsANumberThatIsNotValidForTheRegion()
	{
		// A valid US number is still wrong when the operator is entering GB
		// numbers — IsValidNumberForRegion is what catches it.
		var result = _service.Validate("+1 202 555 0173", "GB");

		Assert.False(result.IsValid);
		Assert.Contains("GB", result.ErrorMessage!, StringComparison.Ordinal);
	}

	[Fact]
	public void Validate_HonoursANonDefaultRegion()
	{
		var result = _service.Validate("202 555 0173", "US");

		Assert.True(result.IsValid);
		Assert.Equal("+12025550173", result.E164);
	}

	[Fact]
	public void Validate_ReadsALocalNumberAgainstTheGivenRegion()
	{
		// Same digits, different region: only one of these is a real number.
		Assert.True(_service.Validate(UkMobile, "GB").IsValid);
		Assert.False(_service.Validate(UkMobile, "US").IsValid);
	}

	[Fact]
	public void GetNumberKind_DistinguishesMobileFromFixedLine()
	{
		Assert.Equal(PhoneNumberKind.Mobile, _service.GetNumberKind(UkMobile));
		Assert.Equal(PhoneNumberKind.FixedLine, _service.GetNumberKind(UkLandline));
	}

	[Fact]
	public void GetNumberKind_ReturnsUnknownForRubbish()
	{
		Assert.Equal(PhoneNumberKind.Unknown, _service.GetNumberKind("banana"));
	}

	[Fact]
	public void FormatHelpers_NormaliseAMessilyTypedNumber()
	{
		const string messy = "  (07912)  345-678 ";

		Assert.Equal("+447912345678", _service.FormatE164(messy));
		Assert.Equal("07912 345678", _service.FormatNational(messy));
		Assert.Equal("+44 7912 345678", _service.FormatInternational(messy));
	}

	[Theory]
	[InlineData("banana")]
	[InlineData("12")]
	public void FormatHelpers_ReturnNullRatherThanThrowingOnBadInput(string input)
	{
		Assert.Null(_service.FormatE164(input));
		Assert.Null(_service.FormatNational(input));
		Assert.Null(_service.FormatInternational(input));
	}
}
