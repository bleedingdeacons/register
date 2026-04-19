using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

/// <summary>
/// Rebuilds the global Serilog pipeline when Better Stack credentials change.
///
/// Serilog's global <c>Log.Logger</c> is a process-wide singleton that captures
/// its sink configuration at construction time — loggers do not pick up config
/// changes retroactively. When the user edits the Better Stack endpoint or
/// source token in Settings, the previously-built pipeline keeps shipping to
/// the old endpoint with the old token forever unless we explicitly tear it
/// down and rebuild.
///
/// This controller owns that rebuild. It keeps hold of a factory for the
/// "base" pipeline (file / console / debug sinks — the stuff that never
/// changes at runtime) so each reconfigure can compose <c>base + optional
/// Better Stack sink</c> from scratch, dispose the previous pipeline, and swap
/// atomically. That avoids two failure modes the naive approach suffers from:
///
///   • Stacking sinks on every save (old Better Stack sink keeps running,
///     new one added on top, file sink fires twice, etc.).
///   • Leaking the shipper loop inside the durable HTTP sink, which runs a
///     background Timer that would otherwise keep hitting the old endpoint.
/// </summary>
public interface IBetterStackLoggerController
{
	/// <summary>
	/// Rebuild <c>Log.Logger</c> using the supplied Better Stack configuration.
	/// Pass a config whose <c>IsValid()</c> returns <c>false</c> to remove the
	/// Better Stack sink entirely and fall back to local sinks only.
	/// Safe to call from any thread.
	/// </summary>
	void Reconfigure(BetterStackConfiguration config);
}