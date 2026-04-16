using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class BaseViewModel : ObservableObject, IQueryAttributable, IDisposable
{
	[ObservableProperty]
	bool isBusy;

	[ObservableProperty]
	string title = string.Empty;

	/// <summary>
	/// Cancellation source for async operations scoped to this ViewModel's lifetime.
	/// Commands should pass <see cref="Token"/> to all async DB/API calls so that
	/// work is cancelled when the user navigates away or the page is disposed.
	/// </summary>
	private CancellationTokenSource? _cts = new();

	/// <summary>
	/// Token that is cancelled when this ViewModel is disposed.
	/// </summary>
	protected CancellationToken Token => _cts?.Token ?? CancellationToken.None;

	public virtual void ApplyQueryAttributes(IDictionary<string, object> query)
	{

	}
	protected async Task ShowFeedback()
	{
		await Task.Delay(100);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}