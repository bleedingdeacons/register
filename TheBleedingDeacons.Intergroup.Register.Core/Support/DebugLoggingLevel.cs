using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// The level everything is logged at, by the tablet's Debug Logging switch:
/// Debug when on, Information when off.
///
/// <para><b>Why a level switch.</b> The pipeline is built with
/// <c>MinimumLevel.ControlledBy(Switch)</c>, so flipping it changes what is
/// logged at once, on every rebuild of the pipeline too, with no restart.
/// The per-source overrides in appsettings.json (<c>Microsoft</c>,
/// <c>System</c> at Warning) still apply over it, so Debug does not bring
/// Entity Framework's SQL with it.</para>
/// </summary>
public sealed class DebugLoggingLevel
{
	public DebugLoggingLevel(bool enabled)
	{
		Switch = new LoggingLevelSwitch(LevelFor(enabled));
	}

	/// <summary>What the pipeline's minimum level is controlled by.</summary>
	public LoggingLevelSwitch Switch { get; }

	/// <summary>Whether Debug lines are being logged.</summary>
	public bool IsEnabled => Switch.MinimumLevel <= LogEventLevel.Debug;

	/// <summary>Turn Debug lines on or off, from the next event.</summary>
	public void Set(bool enabled) => Switch.MinimumLevel = LevelFor(enabled);

	private static LogEventLevel LevelFor(bool enabled) =>
		enabled ? LogEventLevel.Debug : LogEventLevel.Information;
}
