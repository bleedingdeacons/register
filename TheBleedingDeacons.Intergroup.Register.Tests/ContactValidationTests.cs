using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>The inputs a tablet keyboard produces that a validator has to survive.</summary>
public sealed class ContactValidationTests
{
	private readonly PhoneNumberService _phones = new();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("dave")]
	[InlineData("dave@")]
	[InlineData("@example.org")]
	[InlineData("dave@example")]       // no TLD: WordPress's is_email() refuses it too
	[InlineData("dave@example.")]
	[InlineData("dave@.org")]
	public void NotAnAddress(string? email) =>
		Assert.False(EmailValidator.IsValid(email));

	[Theory]
	[InlineData("dave@example.org")]
	[InlineData("  dave@example.org  ")] // trimmed before checking
	[InlineData("dave.b+register@mail.example.co.uk")]
	public void AnAddress(string email) =>
		Assert.True(EmailValidator.IsValid(email));

	/// <summary>
	/// Found, not chosen: EmailAddressAttribute accepts a space in the local
	/// part, and nothing after it checks, so this passes here although
	/// WordPress's is_email() — which the class comment says it mirrors —
	/// refuses it. Pinned so the disagreement is visible.
	/// </summary>
	[Fact]
	public void ASpaceInTheLocalPartIsAccepted() =>
		Assert.True(EmailValidator.IsValid("dave b@example.org"));

	[Fact]
	public void AVeryLongInputDoesNotThrow() =>
		Assert.False(EmailValidator.IsValid(new string('a', 100_000)));

	[Theory]
	[InlineData("gsr@aa-bristol.org", true)]
	[InlineData("GSR@AA-BRISTOL.ORG", true)]
	[InlineData("gsr@mail.aa-bristol.org", true)]
	[InlineData("someone@aa-bristol.org.uk", true)] // a look-alike, caught on purpose
	[InlineData("dave@example.org", false)]
	[InlineData(null, false)]
	public void TheIntergroupsOwnDomain(string? email, bool isIntergroup) =>
		Assert.Equal(isIntergroup, EmailValidator.IsIntergroupAddress(email));

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void AnEmptyNumberSaysSo(string input)
	{
		var result = _phones.Validate(input);

		Assert.False(result.IsValid);
		Assert.Equal("Number is empty.", result.ErrorMessage);
	}

	[Theory]
	[InlineData("not a number")]
	[InlineData("0")]
	[InlineData("077009")]
	public void GarbageIsInvalidNotAnException(string input)
	{
		var result = _phones.Validate(input);

		Assert.False(result.IsValid);
		Assert.Null(result.E164);
		Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
	}

	[Fact]
	public void AValidNumberElsewhereIsNotValidForTheUk()
	{
		var result = _phones.Validate("+1 202 555 0123");

		Assert.False(result.IsValid);
		Assert.Contains("GB", result.ErrorMessage, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("07400 123456")]
	[InlineData("+44 7400 123456")]
	[InlineData("0044 7400 123456")]
	[InlineData("(07400) 123-456")]
	// libphonenumber's own example GB mobile, so it stays valid as the metadata moves.
	public void EveryWayOfWritingAUkMobileFormatsTheSame(string input)
	{
		var result = _phones.Validate(input);

		Assert.True(result.IsValid);
		Assert.Equal("+447400123456", result.E164);
		Assert.Equal(PhoneNumberKind.Mobile, result.Kind);
	}

	/// <summary>
	/// 07700 900000–900999 is Ofcom's range reserved for drama, and
	/// libphonenumber knows it: the number every example reaches for is
	/// not a valid mobile. Worth knowing before writing a test, or typing a
	/// made-up number into a tablet to see what happens.
	/// </summary>
	[Fact]
	public void OfcomsDramaRangeIsNotARealNumber() =>
		Assert.False(_phones.Validate("07700 900123").IsValid);

	[Fact]
	public void TheFormattersReturnNullRatherThanThrowing()
	{
		Assert.Null(_phones.FormatE164("not a number"));
		Assert.Null(_phones.FormatNational("077009"));
		Assert.Null(_phones.FormatInternational(string.Empty));
		Assert.Equal(PhoneNumberKind.Unknown, _phones.GetNumberKind("not a number"));
	}
}
