using Serilog;

namespace TheBleedingDeacons.Intergroup.Register;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell())
		{
			Title = "Intergroup"
		};

		// ── Lifecycle breadcrumbs for reconstructing sessions in Better Stack ──
		//
		// The durable HTTP sink writes each event to its rolling buffer file
		// synchronously as part of the Log.* call — so by the time Log.Information
		// returns, the event is already on disk and safe from process kills.
		// That means we do NOT need to force a flush when the app backgrounds
		// to avoid data loss; the on-disk buffer already covers that case.
		//
		// What IS useful is emitting marker events at lifecycle transitions.
		// When Android force-kills a backgrounded app (which it does silently),
		// the only signal in Better Stack that the session ended is the last
		// buffered event — which might be unrelated noise. A "window stopped"
		// marker gives a clean boundary when reading logs later.
		window.Stopped += (_, _) => TryLog("Window stopped (app backgrounded)");
		window.Destroying += (_, _) => TryLog("Window destroying");

		return window;
	}

	private static void TryLog(string message)
	{
		try { Log.Information(message); }
		catch { /* Never throw from a lifecycle handler. */ }
	}

	protected override void CleanUp()
	{
		Log.Information("Application shutting down");

		// CloseAndFlush drains and disposes every sink, including the durable
		// HTTP sink's shipper loop — giving it a last chance to send anything
		// still on disk before the process exits. Bounded so a slow Better
		// Stack response can't block window-destroy behind HttpClient.Timeout —
		// anything left on disk ships on the next launch.
		MauiProgram.TryFlushLogs();

		base.CleanUp();
	}

	public static Window? MainWindow => Current?.Windows?.FirstOrDefault();

	// Update the root page in the single window (replaces deprecated MainPage setter)
	public static void SetRootPage(Page page)
	{
		if (MainWindow != null)
		{
			MainWindow.Page = page;
		}
	}

	// Get the current page from the single window
	public static Page? CurrentPage => MainWindow?.Page;
}