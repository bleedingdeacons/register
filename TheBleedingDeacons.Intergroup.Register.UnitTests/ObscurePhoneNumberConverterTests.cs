using System.Globalization;
using TheBleedingDeacons.Intergroup.Register.Converters;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Covers <see cref="ObscurePhoneNumberConverter"/>, and doubles as the proof
/// that a test can touch a <c>Microsoft.Maui.Controls</c> type
/// (<c>IValueConverter</c>) in this host. Converters are pure functions of
/// their input, so they need no MAUI runtime — only the reference assemblies.
/// </summary>
public class ObscurePhoneNumberConverterTests
{
	private static object? Convert(ObscurePhoneNumberConverter converter, object? value) =>
		converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

	[Fact]
	public void Convert_KeepsTheLastFourDigitsAndObscuresTheRest()
	{
		var result = Convert(new ObscurePhoneNumberConverter(), "07700900123");

		Assert.Equal("●●●●●●● 0123", result);
	}

	[Fact]
	public void Convert_StripsFormattingBeforeObscuring()
	{
		// Spaces, brackets and dashes are not digits and must not be counted.
		// "4407700900123" is 13 digits, so 9 are obscured and 4 survive.
		var result = Convert(new ObscurePhoneNumberConverter(), "+44 (0)7700 900-123");

		Assert.Equal("●●●●●●●●● 0123", result);
	}

	[Fact]
	public void Convert_ObscuresEverythingWhenThereAreNoMoreDigitsThanTheVisibleCount()
	{
		var result = Convert(new ObscurePhoneNumberConverter(), "0123");

		Assert.Equal("●●●●", result);
	}

	[Fact]
	public void Convert_HonoursCustomObscureCharacterAndVisibleCount()
	{
		var converter = new ObscurePhoneNumberConverter
		{
			ObscureCharacter = '*',
			VisibleDigitsFromEnd = 2,
		};

		Assert.Equal("********* 23", Convert(converter, "07700900123"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void Convert_PassesThroughNullAndEmptyUnchanged(object? value)
	{
		Assert.Equal(value, Convert(new ObscurePhoneNumberConverter(), value));
	}

	[Fact]
	public void ConvertBack_IsNotSupported()
	{
		var converter = new ObscurePhoneNumberConverter();

		Assert.Throws<NotImplementedException>(
			() => converter.ConvertBack("●●●● 0123", typeof(string), null, CultureInfo.InvariantCulture));
	}
}
