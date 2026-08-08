using System.Globalization;
using Microsoft.Maui.Graphics;
using TheBleedingDeacons.Intergroup.Register.Converters;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Utilities;
using TheBleedingDeacons.Intergroup.Register.ViewModels;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// The value converters behind the XAML bindings. Individually trivial, but
/// there are two dozen of them, they decide what the operator sees on screen,
/// and a wrong fallback shows the wrong label rather than failing loudly.
///
/// <para>Every converter's null / wrong-type fallback is covered as well as its
/// happy path, because that fallback is what runs when a binding is still
/// resolving.</para>
///
/// <para><b>Three are not covered, for two different reasons.</b>
/// <c>HasGsrToColorConverter</c> reads <c>Application.Current.Resources</c>,
/// which is null without a running MAUI app.
/// <c>StringToBoolConverter</c> and <c>CountToBoolConverter</c> derive from
/// CommunityToolkit's <c>BaseConverterOneWay</c>, whose <i>constructor</i>
/// calls <c>DispatcherProvider.GetForCurrentThread()</c> and throws
/// <c>REGDB_E_CLASSNOTREG</c> in a console host — they cannot even be
/// instantiated here, let alone exercised. Both are noted in TESTPLAN.md
/// section 4.4.</para>
/// </summary>
public class ConverterTests
{
	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	private static object? Conv(IValueConverter c, object? value, object? parameter = null) =>
		c.Convert(value, typeof(object), parameter, Culture);

	private static object? Back(IValueConverter c, object? value) =>
		c.ConvertBack(value, typeof(object), null, Culture);

	private static object Multi(IMultiValueConverter c, object[] values, object? parameter = null) =>
		c.Convert(values, typeof(object), parameter!, Culture);

	// ── Boolean / equality ────────────────────────────────────────────

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void InvertedBool_Negates(bool input, bool expected)
	{
		var c = new InvertedBoolConverter();

		Assert.Equal(expected, Conv(c, input));
		// Negation is its own inverse, so ConvertBack must mirror Convert.
		Assert.Equal(expected, Back(c, input));
	}

	[Fact]
	public void InvertedBool_FallsBackToFalseForNonBooleans()
	{
		Assert.Equal(false, Conv(new InvertedBoolConverter(), "not a bool"));
		Assert.Equal(false, Conv(new InvertedBoolConverter(), null));
	}

	[Theory]
	[InlineData("a", "a", true)]
	[InlineData("a", "b", false)]
	[InlineData(null, null, true)]
	[InlineData("a", null, false)]
	public void IsEqual_ComparesStringRepresentations(object? value, object? parameter, bool expected)
	{
		Assert.Equal(expected, Conv(new IsEqualConverter(), value, parameter));
		Assert.Equal(!expected, Conv(new IsNotEqualConverter(), value, parameter));
	}

	[Fact]
	public void IsEqual_ComparesAcrossTypesViaToString()
	{
		// Bindings pass ints while XAML parameters are strings.
		Assert.Equal(true, Conv(new IsEqualConverter(), 5, "5"));
	}

	[Theory]
	[InlineData(null, false)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("x", true)]
	public void IsNotNull_TreatsWhitespaceAsAbsent(object? value, bool expected)
	{
		Assert.Equal(expected, Conv(new IsNotNullConverter(), value));
		Assert.Equal(!expected, Conv(new IsNullConverter(), value));
	}

	[Theory]
	[InlineData(true, "Yes")]
	[InlineData(false, "No")]
	public void BoolToYesNo_MapsBothWays(bool value, string expected)
	{
		var c = new BoolToYesNoConverter();

		Assert.Equal(expected, Conv(c, value));
		Assert.Equal(value, Back(c, expected));
	}

	[Fact]
	public void BoolToYesNo_FallsBackToNo()
	{
		Assert.Equal("No", Conv(new BoolToYesNoConverter(), null));
		Assert.Equal(false, Back(new BoolToYesNoConverter(), 42));
	}

	[Fact]
	public void BoolToYesNo_ConvertBackIsCaseInsensitive()
	{
		Assert.Equal(true, Back(new BoolToYesNoConverter(), "yes"));
	}

	[Theory]
	[InlineData(true, "Save")]
	[InlineData(false, "Yes")]
	public void BoolToYesSave_LabelsThePrimaryButton(bool editing, string expected)
	{
		Assert.Equal(expected, Conv(new BoolToYesSaveConverter(), editing));
	}

	[Theory]
	[InlineData(true, "Cancel")]
	[InlineData(false, "Back")]
	public void BoolToCancelBack_LabelsTheSecondaryButton(bool editing, string expected)
	{
		Assert.Equal(expected, Conv(new BoolToCancelBackConverter(), editing));
	}

	[Fact]
	public void ButtonLabelConverters_FallBackToTheNonEditingLabel()
	{
		Assert.Equal("Yes", Conv(new BoolToYesSaveConverter(), null));
		Assert.Equal("Back", Conv(new BoolToCancelBackConverter(), null));
	}

	// ── Countdown ─────────────────────────────────────────────────────

	[Theory]
	[InlineData(5, 1.0)]
	[InlineData(0, 0.0)]
	[InlineData(3, 0.6)]
	public void CountdownToProgress_ScalesFiveSecondsOntoZeroToOne(int countdown, double expected)
	{
		Assert.Equal(expected, (double)Conv(new CountdownToProgressConverter(), countdown)!, 3);
	}

	[Theory]
	[InlineData(99, 1.0)]
	[InlineData(-4, 0.0)]
	public void CountdownToProgress_ClampsOutOfRangeValues(int countdown, double expected)
	{
		Assert.Equal(expected, (double)Conv(new CountdownToProgressConverter(), countdown)!, 3);
	}

	[Fact]
	public void CountdownToProgress_FallsBackToFullForNonIntegers()
	{
		Assert.Equal(1.0, (double)Conv(new CountdownToProgressConverter(), "x")!, 3);
	}

	// ── Multi-value: editing / validation visibility ──────────────────

	[Theory]
	[InlineData(false, false, true)]   // not editing, no errors → show the question
	[InlineData(true, false, false)]
	[InlineData(false, true, false)]
	[InlineData(true, true, false)]
	public void QuestionVisibility_ShowsOnlyWhenIdleAndValid(bool editing, bool errors, bool expected)
	{
		Assert.Equal(expected, Multi(new QuestionVisibilityConverter(), new object[] { editing, errors }));
	}

	[Theory]
	[InlineData(false, true, true)]    // not editing, has errors → show the validation
	[InlineData(false, false, false)]
	[InlineData(true, true, false)]
	public void ValidationVisibility_ShowsOnlyWhenIdleAndInvalid(bool editing, bool errors, bool expected)
	{
		Assert.Equal(expected, Multi(new ValidationVisibilityConverter(), new object[] { editing, errors }));
	}

	[Fact]
	public void EditingVisibilityConverters_RejectTheWrongNumberOfValues()
	{
		Assert.Equal(false, Multi(new QuestionVisibilityConverter(), new object[] { true }));
		Assert.Equal(false, Multi(new ValidationVisibilityConverter(), new object[] { true, false, true }));
	}

	[Theory]
	[InlineData(new object[] { "", "x" }, true)]
	[InlineData(new object[] { "", "   " }, false)]
	[InlineData(new object[] { "", "" }, false)]
	public void StringNotEmpty_IsTrueWhenAnyValueHasContent(object[] values, bool expected)
	{
		Assert.Equal(expected, Multi(new StringNotEmptyConverter(), values));
	}

	[Fact]
	public void StringNotEmpty_HandlesNoValuesAtAll()
	{
		Assert.Equal(false, Multi(new StringNotEmptyConverter(), Array.Empty<object>()));
	}

	[Theory]
	[InlineData("x", false, true)]
	[InlineData("x", true, false)]
	[InlineData("", false, false)]
	public void StringNotEmptyAndNotEditing_CombinesBothConditions(string s, bool editing, bool expected)
	{
		Assert.Equal(expected, Multi(new StringNotEmptyAndNotEditingConverter(), new object[] { s, editing }));
	}

	[Theory]
	[InlineData("x", true, true)]
	[InlineData("x", false, false)]
	[InlineData("", true, false)]
	public void StringNotEmptyAndEditing_CombinesBothConditions(string s, bool editing, bool expected)
	{
		Assert.Equal(expected, Multi(new StringNotEmptyAndEditingConverter(), new object[] { s, editing }));
	}

	// ── Multi-value: generic-email visibility ─────────────────────────

	[Theory]
	[InlineData(false, "a@b.com", true)]
	[InlineData(true, "a@b.com", false)]
	[InlineData(false, "", false)]
	public void UsingGenericVisibility_ShowsTheLabelWhenIdleWithAnEmail(bool editing, string email, bool expected)
	{
		Assert.Equal(expected, Multi(new UsingGenericVisibilityConverter(), new object[] { editing, email }));
	}

	[Theory]
	[InlineData(true, "a@b.com", true)]
	[InlineData(false, "a@b.com", false)]
	[InlineData(true, "", false)]
	public void UsingGenericEditVisibility_ShowsTheCheckboxWhenEditingWithAnEmail(bool editing, string email, bool expected)
	{
		Assert.Equal(expected, Multi(new UsingGenericEditVisibilityConverter(), new object[] { editing, email }));
	}

	[Theory]
	[InlineData("label", false, "shown@b.com", "", true)]
	[InlineData("label", true, "shown@b.com", "", false)]
	[InlineData("checkbox", true, "", "edit@b.com", true)]
	[InlineData("checkbox", false, "", "edit@b.com", false)]
	[InlineData("labeltext", true, "", "edit@b.com", true)]
	[InlineData("labeltext", false, "shown@b.com", "", true)]
	[InlineData("labeltext", false, "", "edit@b.com", false)]
	public void UsingGenericUnified_SwitchesOnTheControlTypeParameter(
		string control, bool editing, string display, string edit, bool expected)
	{
		Assert.Equal(expected, Multi(
			new UsingGenericUnifiedVisibilityConverter(),
			new object[] { editing, display, edit },
			control));
	}

	[Fact]
	public void UsingGenericUnified_IsCaseInsensitiveOnTheParameter()
	{
		Assert.Equal(true, Multi(
			new UsingGenericUnifiedVisibilityConverter(),
			new object[] { false, "a@b.com", "" },
			"LABEL"));
	}

	[Fact]
	public void UsingGenericUnified_HidesForAnUnknownControlType()
	{
		Assert.Equal(false, Multi(
			new UsingGenericUnifiedVisibilityConverter(),
			new object[] { false, "a@b.com", "" },
			"something-else"));
	}

	[Fact]
	public void GenericEmailConverters_RejectTheWrongNumberOfValues()
	{
		Assert.Equal(false, Multi(new UsingGenericVisibilityConverter(), new object[] { true }));
		Assert.Equal(false, Multi(new UsingGenericEditVisibilityConverter(), new object[] { true }));
		Assert.Equal(false, Multi(new UsingGenericUnifiedVisibilityConverter(), new object[] { true, "a" }, "label"));
	}

	// ── Colour and status converters ──────────────────────────────────

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void BoolToColor_UsesRedForErrorAndGreenOtherwise(bool isError)
	{
		var expected = isError ? Colors.Red : Colors.Green;

		Assert.Equal(expected, Conv(new BoolToColorConverter(), isError));
	}

	[Fact]
	public void BoolToColor_FallsBackToGray()
	{
		Assert.Equal(Colors.Gray, Conv(new BoolToColorConverter(), null));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void OnlineStatusToColor_GreenWhenOnline(bool online)
	{
		Assert.Equal(online ? Colors.Green : Colors.Red, Conv(new OnlineStatusToColorConverter(), online));
	}

	[Theory]
	[InlineData(true, "Online")]
	[InlineData(false, "Offline")]
	public void OnlineStatusToText_LabelsTheState(bool online, string expected)
	{
		Assert.Equal(expected, Conv(new OnlineStatusToTextConverter(), online));
	}

	[Fact]
	public void OnlineStatusConverters_FallBackToUnknown()
	{
		Assert.Equal("Unknown", Conv(new OnlineStatusToTextConverter(), null));
		Assert.Equal(Colors.Gray, Conv(new OnlineStatusToColorConverter(), null));
		Assert.Equal("❓", Conv(new OnlineStatusToIconConverter(), null));
	}

	[Theory]
	[InlineData(true, "📶")]
	[InlineData(false, "📴")]
	public void OnlineStatusToIcon_PicksTheSignalGlyph(bool online, string expected)
	{
		Assert.Equal(expected, Conv(new OnlineStatusToIconConverter(), online));
	}

	[Theory]
	[InlineData(EmailStatus.Sent)]
	[InlineData(EmailStatus.Failed)]
	[InlineData(EmailStatus.Pending)]
	[InlineData(EmailStatus.Sending)]
	[InlineData(EmailStatus.Cancelled)]
	public void EmailStatusToColor_GivesEveryStatusADistinctMeaning(EmailStatus status)
	{
		var expected = status switch
		{
			EmailStatus.Sent => Colors.Green,
			EmailStatus.Failed => Colors.Red,
			EmailStatus.Pending => Colors.Orange,
			EmailStatus.Sending => Colors.Blue,
			_ => Colors.Gray,
		};

		Assert.Equal(expected, Conv(new EmailStatusToColorConverter(), status));
	}

	[Fact]
	public void EmailStatusToColor_FallsBackToGrayForANonStatus()
	{
		Assert.Equal(Colors.Gray, Conv(new EmailStatusToColorConverter(), "Sent"));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void DaySelectionConverters_HighlightTheSelectedDay(bool selected)
	{
		Assert.Equal(selected ? Colors.LightBlue : Colors.Transparent,
			Conv(new SelectedBackgroundConverter(), selected));
		Assert.Equal(selected ? Colors.Blue : Colors.Gray,
			Conv(new SelectedBorderConverter(), selected));
		Assert.Equal(selected ? Colors.DarkBlue : Colors.Black,
			Conv(new SelectedTextConverter(), selected));
	}

	[Fact]
	public void DaySelectionConverters_FallBackToTheUnselectedLook()
	{
		Assert.Equal(Colors.Transparent, Conv(new SelectedBackgroundConverter(), null));
		Assert.Equal(Colors.Gray, Conv(new SelectedBorderConverter(), null));
		Assert.Equal(Colors.Black, Conv(new SelectedTextConverter(), null));
	}

	[Theory]
	[InlineData(true, "Testing Connection...")]
	[InlineData(false, "Test Connection")]
	public void BoolToTestButtonText_ReflectsTheInFlightState(bool busy, string expected)
	{
		var actual = Conv(new BoolToTestButtonTextConverter(), busy);

		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(true, "Saving...")]
	[InlineData(false, "Save Settings")]
	public void BoolToSaveButtonText_ReflectsTheInFlightState(bool busy, string expected)
	{
		Assert.Equal(expected, Conv(new BoolToSaveButtonTextConverter(), busy));
	}

	// ── Database file selection ───────────────────────────────────────

	[Fact]
	public void DatabaseFileToPath_ProjectsTheFullPath()
	{
		var file = new DatabaseFileInfo { FullPath = @"C:\data\unity.db", FileName = "unity.db" };

		Assert.Equal(@"C:\data\unity.db", Conv(new DatabaseFileToPathConverter(), file));
	}

	[Fact]
	public void DatabaseFileToPath_PassesThroughAnythingElseUnchanged()
	{
		var c = new DatabaseFileToPathConverter();

		Assert.Equal("already a path", Conv(c, "already a path"));
		// ConvertBack is a deliberate pass-through, not a throw.
		Assert.Equal("x", Back(c, "x"));
	}

	// ── ConvertBack is unsupported on the one-way converters ──────────

	public static TheoryData<IValueConverter> OneWayConverters => new()
	{
		new IsEqualConverter(),
		new IsNotEqualConverter(),
		new IsNotNullConverter(),
		new IsNullConverter(),
		new CountdownToProgressConverter(),
		new BoolToYesSaveConverter(),
		new BoolToCancelBackConverter(),
		new BoolToColorConverter(),
		new BoolToTestButtonTextConverter(),
		new BoolToSaveButtonTextConverter(),
		new OnlineStatusToColorConverter(),
		new OnlineStatusToTextConverter(),
		new OnlineStatusToIconConverter(),
		new EmailStatusToColorConverter(),
		new SelectedBackgroundConverter(),
		new SelectedBorderConverter(),
		new SelectedTextConverter(),
	};

	[Theory]
	[MemberData(nameof(OneWayConverters))]
	public void OneWayConverter_RejectsConvertBack(IValueConverter converter)
	{
		Assert.Throws<NotImplementedException>(() => Back(converter, true));
	}

	public static TheoryData<IMultiValueConverter> OneWayMultiConverters => new()
	{
		new QuestionVisibilityConverter(),
		new ValidationVisibilityConverter(),
		new StringNotEmptyConverter(),
		new UsingGenericVisibilityConverter(),
		new UsingGenericEditVisibilityConverter(),
		new UsingGenericUnifiedVisibilityConverter(),
		new StringNotEmptyAndNotEditingConverter(),
		new StringNotEmptyAndEditingConverter(),
	};

	[Theory]
	[MemberData(nameof(OneWayMultiConverters))]
	public void OneWayMultiConverter_RejectsConvertBack(IMultiValueConverter converter)
	{
		Assert.Throws<NotImplementedException>(
			() => converter.ConvertBack(true, new[] { typeof(bool) }, null!, Culture));
	}
}
