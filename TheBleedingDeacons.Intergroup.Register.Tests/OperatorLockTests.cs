using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// When the operator's pages ask for the tablet's screen code: which routes
/// are guarded, how long a correct code lasts, and what closes it early.
/// </summary>
public sealed class OperatorLockTests
{
	private static readonly string[] Guarded =
		["AdminPage", "SettingsPage", "MailSettingsPage", "ApiSettingsPage", "DiagnosticDumpPage", "EmailStatusPage"];

	private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

	private readonly ManualTimeProvider _time = new();

	private OperatorLock Lock() => new(Guarded, _time, Window);

	[Theory]
	[InlineData("//AdminPage")]
	[InlineData("//SettingsPage")]
	[InlineData("//SettingsPage/MailSettingsPage")]
	[InlineData("MailSettingsPage")]
	[InlineData("DiagnosticDumpPage?file=unity.db")]
	[InlineData("//SettingsPage/EmailStatusPage/")]
	public void An_operators_page_is_guarded_wherever_it_appears_in_the_route(string location)
	{
		Assert.True(Lock().Guards(location));
	}

	[Theory]
	[InlineData("//MainPage")]
	[InlineData("//RegistrationOverviewPage")]
	[InlineData("//MainPage/VerifyGroupPage/EditGroupPage")]
	[InlineData("..")]
	[InlineData("")]
	[InlineData(null)]
	public void An_attendee_page_is_not_guarded(string? location)
	{
		Assert.False(Lock().Guards(location));
	}

	[Theory]
	[InlineData("//adminpage")]
	[InlineData("//MainPage?next=AdminPage")]
	[InlineData("//AdminPageExtra")]
	public void Only_an_exact_route_segment_is_guarded(string location)
	{
		Assert.False(Lock().Guards(location));
	}

	[Fact]
	public void A_new_lock_is_closed()
	{
		var gate = Lock();

		Assert.False(gate.IsOpen);
		Assert.False(gate.TryPass());
	}

	[Fact]
	public void A_correct_code_opens_it()
	{
		var gate = Lock();

		gate.Open();

		Assert.True(gate.IsOpen);
		Assert.True(gate.TryPass());
	}

	[Fact]
	public void It_is_still_open_just_before_the_window_ends()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(Window - TimeSpan.FromSeconds(1));

		Assert.True(gate.TryPass());
	}

	[Fact]
	public void It_closes_when_the_window_ends()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(Window);

		Assert.False(gate.IsOpen);
		Assert.False(gate.TryPass());
	}

	[Fact]
	public void Each_step_among_the_operators_pages_starts_the_window_again()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.True(gate.TryPass());

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.True(gate.TryPass());

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.True(gate.IsOpen);
	}

	[Fact]
	public void A_refused_step_does_not_reopen_it_later()
	{
		var gate = Lock();
		gate.Open();
		_time.Advance(Window + TimeSpan.FromMinutes(1));

		Assert.False(gate.TryPass());

		_time.Advance(-TimeSpan.FromMinutes(2));
		Assert.False(gate.TryPass());
	}

	[Fact]
	public void Closing_it_asks_again_however_much_of_the_window_was_left()
	{
		var gate = Lock();
		gate.Open();

		gate.Close();

		Assert.False(gate.IsOpen);
		Assert.False(gate.TryPass());
	}

	[Fact]
	public void Looking_at_it_does_not_extend_it()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.True(gate.IsOpen);

		_time.Advance(TimeSpan.FromMinutes(1));
		Assert.False(gate.IsOpen);
	}

	[Fact]
	public void The_default_window_is_five_minutes()
	{
		var gate = new OperatorLock(Guarded, _time);
		gate.Open();

		_time.Advance(OperatorLock.DefaultWindow - TimeSpan.FromSeconds(1));
		Assert.True(gate.IsOpen);

		_time.Advance(TimeSpan.FromSeconds(1));
		Assert.False(gate.IsOpen);
	}

	[Fact]
	public void A_window_of_no_length_is_refused()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new OperatorLock(Guarded, _time, TimeSpan.Zero));
	}

	[Fact]
	public void A_grace_of_no_length_is_refused()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new OperatorLock(Guarded, _time, Window, TimeSpan.Zero));
	}

	[Fact]
	public void A_short_trip_to_the_background_keeps_it_open()
	{
		var gate = Lock();
		gate.Open();

		gate.Leave();
		_time.Advance(OperatorLock.DefaultGrace - TimeSpan.FromSeconds(1));
		gate.Return();

		Assert.True(gate.TryPass());
	}

	[Fact]
	public void A_long_trip_to_the_background_closes_it_though_the_window_had_time_left()
	{
		var gate = Lock();
		gate.Open();

		gate.Leave();
		_time.Advance(OperatorLock.DefaultGrace);
		gate.Return();

		Assert.False(gate.IsOpen);
		Assert.False(gate.TryPass());
	}

	[Fact]
	public void A_short_trip_does_not_stretch_the_window()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(TimeSpan.FromMinutes(4));
		gate.Leave();
		_time.Advance(TimeSpan.FromMinutes(1));
		gate.Return();

		Assert.False(gate.IsOpen);
	}

	[Fact]
	public void The_trip_is_timed_from_when_the_app_first_left()
	{
		var gate = Lock();
		gate.Open();

		gate.Leave();
		_time.Advance(TimeSpan.FromMinutes(1));
		gate.Leave();
		_time.Advance(TimeSpan.FromMinutes(1));
		gate.Return();

		Assert.False(gate.IsOpen);
	}

	[Fact]
	public void Each_trip_is_timed_on_its_own()
	{
		var gate = Lock();
		gate.Open();

		gate.Leave();
		_time.Advance(TimeSpan.FromMinutes(1));
		gate.Return();
		Assert.True(gate.TryPass());

		gate.Leave();
		_time.Advance(TimeSpan.FromMinutes(1));
		gate.Return();

		Assert.True(gate.IsOpen);
	}

	[Fact]
	public void Coming_back_without_having_left_changes_nothing()
	{
		var gate = Lock();
		gate.Open();

		_time.Advance(TimeSpan.FromMinutes(3));
		gate.Return();

		Assert.True(gate.IsOpen);
	}
}
