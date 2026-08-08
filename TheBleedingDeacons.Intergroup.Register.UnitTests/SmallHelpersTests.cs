using System.ComponentModel;
using System.Globalization;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Support.BetterStackDurable;
using TheBleedingDeacons.Intergroup.Register.Utilities;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Small pure helpers that sit on real code paths: online-meeting
/// classification, the criteria TypeConverter, the local data container, the
/// fire-and-forget guard, and the Better Stack batch formatter.
/// </summary>
public class MeetingExtensionsTests
{
	[Fact]
	public void IsOnline_IsTrueWhenTheFlagIsSet()
	{
		Assert.True(new Meeting { IsOnline = true }.IsOnline());
	}

	[Theory]
	[InlineData("online")]
	[InlineData("Online")]
	[InlineData("ONLINE")]
	[InlineData("Discussion, Online, Open")]
	public void IsOnline_IsTrueWhenTheTypesListSaysSo(string types)
	{
		Assert.True(new Meeting { IsOnline = false, Types = types }.IsOnline());
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Discussion, Open")]
	public void IsOnline_IsFalseForAnInPersonMeeting(string? types)
	{
		Assert.False(new Meeting { IsOnline = false, Types = types }.IsOnline());
	}

	[Fact]
	public void IsOnline_MatchesOnASubstring()
	{
		// Pinning current behaviour: the check is a substring test, not a
		// token match, so a hypothetical type like "Onlineish" counts as
		// online. Harmless with today's tag vocabulary, but it is the kind of
		// thing that surprises later.
		Assert.True(new Meeting { IsOnline = false, Types = "Onlineish" }.IsOnline());
	}

	[Fact]
	public void IsOnline_RejectsANullMeeting()
	{
		Meeting? meeting = null;

		Assert.Throws<ArgumentNullException>(() => meeting!.IsOnline());
	}
}

/// <summary>
/// <see cref="MeetingCriteriaConverter"/> — a <see cref="TypeConverter"/> used
/// from XAML. Two defects are pinned here rather than fixed, because which
/// separator is correct depends on the XAML that consumes it.
/// </summary>
public class MeetingCriteriaConverterTests
{
	private readonly MeetingCriteriaConverter _converter = new();

	[Fact]
	public void CanConvertFrom_AcceptsStrings()
	{
		Assert.True(_converter.CanConvertFrom(null, typeof(string)));
	}

	[Fact]
	public void ConvertFrom_SplitsOnAComma()
	{
		var result = (MeetingCriteria)_converter.ConvertFrom(null, CultureInfo.InvariantCulture, "Monday,Discussion")!;

		Assert.Equal("Monday", result.Day);
		Assert.Equal("Discussion", result.MeetingType);
	}

	[Fact]
	public void ConvertTo_JoinsWithAPipe()
	{
		var criteria = new MeetingCriteria { Day = "Monday", MeetingType = "Discussion" };

		var result = _converter.ConvertTo(null, CultureInfo.InvariantCulture, criteria, typeof(string));

		Assert.Equal("Monday|Discussion", result);
	}

	[Fact]
	public void RoundTrip_Throws_BecauseTheSeparatorsDisagree()
	{
		// DEFECT (pinned, not fixed): ConvertTo joins with '|' but ConvertFrom
		// splits on ',', so the converter's own output is not valid input to
		// itself. The two bugs compound — with no comma present, the unguarded
		// parts[1] index throws — so a round trip does not merely lose data, it
		// crashes. Fixing it means choosing a separator, which depends on the
		// XAML that consumes it. See TESTPLAN.md section 2.8.
		var original = new MeetingCriteria { Day = "Monday", MeetingType = "Discussion" };

		var text = (string)_converter.ConvertTo(null, CultureInfo.InvariantCulture, original, typeof(string))!;
		Assert.Equal("Monday|Discussion", text);

		Assert.Throws<IndexOutOfRangeException>(
			() => _converter.ConvertFrom(null, CultureInfo.InvariantCulture, text));
	}

	[Fact]
	public void ConvertFrom_ThrowsOnAStringWithoutASeparator()
	{
		// DEFECT (pinned, not fixed): parts[1] is indexed unguarded, so any
		// value lacking a comma throws IndexOutOfRangeException rather than
		// failing gracefully.
		Assert.Throws<IndexOutOfRangeException>(
			() => _converter.ConvertFrom(null, CultureInfo.InvariantCulture, "Monday"));
	}
}

public class RegisterDataTests
{
	[Fact]
	public void NewInstance_StartsEmptyRatherThanNull()
	{
		var data = new RegisterData();

		Assert.Empty(data.Groups);
		Assert.Equal(0, data.TotalGroups);
		Assert.Equal(0, data.TotalMeetings);
		Assert.Equal(0, data.TotalPositions);
		Assert.Equal(0, data.TotalMembers);
		Assert.Equal(0, data.TotalIntergroupMeetings);
	}

	[Fact]
	public void Totals_TrackTheUnderlyingCollections()
	{
		var data = new RegisterData(
			new List<Group> { new(), new() },
			new List<Meeting> { new() },
			new List<Position> { new(), new(), new() },
			new List<Member>(),
			new List<IntergroupMeeting> { new() });

		Assert.Equal(2, data.TotalGroups);
		Assert.Equal(1, data.TotalMeetings);
		Assert.Equal(3, data.TotalPositions);
		Assert.Equal(0, data.TotalMembers);
		Assert.Equal(1, data.TotalIntergroupMeetings);
	}

	[Fact]
	public void Constructor_SubstitutesEmptyListsForNulls()
	{
		var data = new RegisterData(null!, null!, null!, null!, null!);

		Assert.NotNull(data.Groups);
		Assert.NotNull(data.Meetings);
		Assert.NotNull(data.Positions);
		Assert.NotNull(data.Members);
		Assert.NotNull(data.IntergroupMeetings);
	}
}

public class TaskExtensionsTests
{
	[Fact]
	public async Task SafeFireAndForget_SwallowsTheExceptionAndReportsIt()
	{
		// The point of the helper: a discarded task's failure must be observed
		// rather than surfacing later as an UnobservedTaskException.
		var observed = new TaskCompletionSource<Exception>();

		Task.FromException(new InvalidOperationException("boom"))
			.SafeFireAndForget("test", observed.SetResult);

		var ex = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.IsType<InvalidOperationException>(ex);
		Assert.Equal("boom", ex.Message);
	}

	[Fact]
	public async Task SafeFireAndForget_DoesNotInvokeTheHandlerOnSuccess()
	{
		var called = false;

		Task.CompletedTask.SafeFireAndForget("test", _ => called = true);
		await Task.Delay(50);

		Assert.False(called);
	}

	[Fact]
	public async Task RunSafeFireAndForget_ReportsAFailureFromThreadPoolWork()
	{
		var observed = new TaskCompletionSource<Exception>();

		Support.TaskExtensions.RunSafeFireAndForget(
			() => throw new InvalidOperationException("boom"), "test", observed.SetResult);

		var ex = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal("boom", ex.Message);
	}

	[Fact]
	public void RunSafeFireAndForget_RejectsNullWork()
	{
		Assert.Throws<ArgumentNullException>(() => Support.TaskExtensions.RunSafeFireAndForget(null!));
	}
}

public class BetterStackNdjsonBatchFormatterTests
{
	[Fact]
	public void Format_WritesOnePreRenderedEventPerLine()
	{
		// Events replayed from the durable buffer arrive as JSON strings and
		// are passed through untouched — Better Stack wants NDJSON, not an array.
		var formatter = new BetterStackNdjsonBatchFormatter();
		using var writer = new StringWriter();

		formatter.Format(new[] { "{\"a\":1}", "{\"b\":2}" }, writer);

		var lines = writer.ToString()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(l => l.TrimEnd('\r'))
			.ToArray();

		Assert.Equal(new[] { "{\"a\":1}", "{\"b\":2}" }, lines);
		Assert.DoesNotContain("[", writer.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void Format_SkipsBlankEntries(string blank)
	{
		var formatter = new BetterStackNdjsonBatchFormatter();
		using var writer = new StringWriter();

		formatter.Format(new[] { "{\"a\":1}", blank }, writer);

		Assert.Single(writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public void Format_TreatsANullBatchAsNothingToSend()
	{
		var formatter = new BetterStackNdjsonBatchFormatter();
		using var writer = new StringWriter();

		formatter.Format((IEnumerable<string>)null!, writer);

		Assert.Equal(string.Empty, writer.ToString());
	}
}
