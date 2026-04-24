using Serilog;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Extension methods for safe fire-and-forget async patterns.
/// Ensures exceptions from discarded tasks are always logged
/// rather than silently swallowed or causing UnobservedTaskException.
/// </summary>
public static class TaskExtensions
{
	private static readonly ILogger Logger = AppLogger.ForContext(nameof(TaskExtensions));

	/// <summary>
	/// Executes a task without awaiting it, logging any exception that occurs.
	/// Use this instead of <c>_ = SomeAsync()</c> to ensure errors are observed.
	/// </summary>
	public static async void SafeFireAndForget(
		this Task task,
		string? context = null,
		Action<Exception>? onException = null)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Unobserved exception in fire-and-forget task{Context}",
				string.IsNullOrEmpty(context) ? "" : $" ({context})");

			onException?.Invoke(ex);
		}
	}

	/// <summary>
	/// Runs <paramref name="work"/> on a thread-pool thread and fires-and-forgets
	/// the resulting task, logging any exception. Use this in place of
	/// <c>_ = Task.Run(async () =&gt; await DoWorkAsync())</c> so failures aren't
	/// silently dropped.
	/// </summary>
	public static void RunSafeFireAndForget(
		Func<Task> work,
		string? context = null,
		Action<Exception>? onException = null)
	{
		ArgumentNullException.ThrowIfNull(work);
		Task.Run(work).SafeFireAndForget(context, onException);
	}
}