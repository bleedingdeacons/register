using TheBleedingDeacons.Intergroup.Register.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// <see cref="EmailValidator"/> exists to reject at the input boundary what
/// WordPress's <c>is_email()</c> would reject at member-create time, hours
/// later, during background reconciliation. The TLD cases below are the whole
/// point of the class — <see cref="System.ComponentModel.DataAnnotations.EmailAddressAttribute"/>
/// on its own accepts every one of them.
/// </summary>
public class EmailValidatorTests
{
	[Theory]
	[InlineData("gsr@example.com")]
	[InlineData("first.last@example.co.uk")]
	[InlineData("name+tag@sub.example.org")]
	public void IsValid_AcceptsAddressesWithATld(string email)
	{
		Assert.True(EmailValidator.IsValid(email));
	}

	[Theory]
	[InlineData("thorn@thorn")]        // no dot in the domain — the bug this class was written for
	[InlineData("thorn@thorn.")]       // trailing dot, empty TLD
	[InlineData("thorn@.com")]         // empty label before the dot
	public void IsValid_RejectsDomainsWithoutAUsableTld(string email)
	{
		Assert.False(EmailValidator.IsValid(email));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not-an-address")]
	[InlineData("@example.com")]
	public void IsValid_RejectsEmptyAndMalformedInput(string? email)
	{
		Assert.False(EmailValidator.IsValid(email));
	}

	[Fact]
	public void IsValid_IgnoresSurroundingWhitespace()
	{
		Assert.True(EmailValidator.IsValid("  gsr@example.com  "));
	}
}
