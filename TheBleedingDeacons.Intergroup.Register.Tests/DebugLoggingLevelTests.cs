using Serilog;
using Serilog.Core;
using Serilog.Events;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The Debug Logging switch: Debug lines on or off at once, and the
/// per-source overrides from appsettings.json still over it.
/// </summary>
public sealed class DebugLoggingLevelTests
{
	private sealed class Collecting : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}

	// As MauiProgram builds it: appsettings.json's level and overrides, then
	// the switch over the level.
	private static Logger Build(DebugLoggingLevel level, ILogEventSink sink) =>
		new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.MinimumLevel.ControlledBy(level.Switch)
			.WriteTo.Sink(sink)
			.CreateLogger();

	[Fact]
	public void On_Debug_lines_are_logged()
	{
		var sink = new Collecting();
		using var logger = Build(new DebugLoggingLevel(enabled: true), sink);

		logger.Debug("detail");

		Assert.Single(sink.Events);
	}

	[Fact]
	public void Off_only_Information_and_up_are_logged()
	{
		var sink = new Collecting();
		using var logger = Build(new DebugLoggingLevel(enabled: false), sink);

		logger.Debug("detail");
		logger.Information("news");

		Assert.Equal(LogEventLevel.Information, Assert.Single(sink.Events).Level);
	}

	[Fact]
	public void Switching_takes_effect_at_once_without_a_new_logger()
	{
		var level = new DebugLoggingLevel(enabled: true);
		var sink = new Collecting();
		using var logger = Build(level, sink);

		level.Set(false);
		logger.Debug("dropped");
		level.Set(true);
		logger.Debug("kept");

		Assert.Equal("kept", Assert.Single(sink.Events).MessageTemplate.Text);
		Assert.True(level.IsEnabled);
	}

	[Fact]
	public void The_overrides_still_hold_a_noisy_source_back()
	{
		var sink = new Collecting();
		using var logger = Build(new DebugLoggingLevel(enabled: true), sink);

		logger.ForContext(Constants.SourceContextPropertyName, "Microsoft.EntityFrameworkCore.Database.Command").Debug("SELECT ...");

		Assert.Empty(sink.Events);
	}
}
