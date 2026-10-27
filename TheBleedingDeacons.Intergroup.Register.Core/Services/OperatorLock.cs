namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Whether the operator's pages — Administration, Settings and everything
/// under them — may be opened without asking for the tablet's screen code.
///
/// <para><b>Why there is a lock at all.</b> The tablet is passed round a
/// meeting. Before this, anyone holding it could open Administration and
/// Reset Device, or open Settings and copy the database, every member's
/// contact details included, to the public Downloads folder. The tablet's
/// own screen code is something the operator already has and the room does
/// not, so asking for it costs nothing to set up.</para>
///
/// <para><b>The window slides.</b> A correct code opens the operator's pages
/// for <see cref="DefaultWindow"/>, and each further step among them
/// (Settings to Mail Settings, back to Settings) starts it again, so an
/// operator working through the settings is not asked twice. Leaving for an
/// attendee page closes it at once: a tablet handed back to the room is
/// locked whether or not anybody remembered to wait.</para>
///
/// <para><b>Going to the background closes it too, after a grace.</b> Some
/// of what an operator does leaves the app for a moment: the screen-lock
/// prompt itself on older Android, and Freedom's sign-in in the browser.
/// Away for less than <see cref="DefaultGrace"/> keeps the window; any
/// longer and the code is asked for again.</para>
///
/// <para>This decides only when to ask. Asking is the platform's, in the app,
/// because the screen-lock prompt is Android's own.</para>
/// </summary>
public sealed class OperatorLock
{
	/// <summary>How long a correct code keeps the operator's pages open between steps.</summary>
	public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

	/// <summary>How long the app may be in the background without closing the window.</summary>
	public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(2);

	private readonly HashSet<string> _guarded;
	private readonly TimeProvider _time;
	private readonly TimeSpan _window;
	private readonly TimeSpan _grace;
	private readonly Lock _sync = new();
	private DateTimeOffset? _openUntil;
	private DateTimeOffset? _leftAt;

	/// <param name="guardedRoutes">The Shell route names that need the code.</param>
	/// <param name="time">The clock; the system's unless a test supplies one.</param>
	/// <param name="window">How long a correct code lasts; <see cref="DefaultWindow"/> unless given.</param>
	/// <param name="grace">How long the app may be away; <see cref="DefaultGrace"/> unless given.</param>
	public OperatorLock(
		IEnumerable<string> guardedRoutes,
		TimeProvider? time = null,
		TimeSpan? window = null,
		TimeSpan? grace = null)
	{
		ArgumentNullException.ThrowIfNull(guardedRoutes);

		_window = Positive(window ?? DefaultWindow, nameof(window));
		_grace = Positive(grace ?? DefaultGrace, nameof(grace));
		_guarded = new HashSet<string>(guardedRoutes, StringComparer.Ordinal);
		_time = time ?? TimeProvider.System;
	}

	/// <summary>True while a correct code is still in force.</summary>
	public bool IsOpen
	{
		get
		{
			lock (_sync)
			{
				return _openUntil is { } until && _time.GetUtcNow() < until;
			}
		}
	}

	/// <summary>
	/// Whether a Shell location reaches an operator's page. Any segment
	/// counts, so <c>//SettingsPage/MailSettingsPage</c> and a bare
	/// <c>MailSettingsPage</c> pushed from Settings are both guarded; a
	/// query string is not part of the route.
	/// </summary>
	public bool Guards(string? location)
	{
		if (string.IsNullOrWhiteSpace(location))
			return false;

		var query = location.IndexOf('?', StringComparison.Ordinal);
		var path = query < 0 ? location : location[..query];

		return path
			.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(_guarded.Contains);
	}

	/// <summary>
	/// Let a step among the operator's pages through without asking, if the
	/// window is still open, and start the window again. False means ask.
	/// </summary>
	public bool TryPass()
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			if (_openUntil is not { } until || now >= until)
			{
				_openUntil = null;
				return false;
			}

			_openUntil = now + _window;
			return true;
		}
	}

	/// <summary>A correct code was given: open the window.</summary>
	public void Open()
	{
		lock (_sync)
		{
			_openUntil = _time.GetUtcNow() + _window;
		}
	}

	/// <summary>Close the window now: the next operator's page asks again.</summary>
	public void Close()
	{
		lock (_sync)
		{
			_openUntil = null;
		}
	}

	/// <summary>The app went to the background.</summary>
	public void Leave()
	{
		lock (_sync)
		{
			_leftAt ??= _time.GetUtcNow();
		}
	}

	/// <summary>
	/// The app came back. Closes the window if it was away for
	/// <see cref="DefaultGrace"/> or longer; otherwise the window carries on
	/// as if the app had never left, and still ends when it would have.
	/// </summary>
	public void Return()
	{
		lock (_sync)
		{
			if (_leftAt is { } left && _time.GetUtcNow() - left >= _grace)
				_openUntil = null;

			_leftAt = null;
		}
	}

	private static TimeSpan Positive(TimeSpan span, string name) =>
		span > TimeSpan.Zero
			? span
			: throw new ArgumentOutOfRangeException(name, span, "Must be longer than zero.");
}
