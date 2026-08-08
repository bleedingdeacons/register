using Microsoft.Maui.Storage;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests.Fakes;

/// <summary>
/// In-memory <see cref="IPreferences"/>. Stands in for the platform prefs
/// store, which cannot be reached from a console test host.
///
/// <para><see cref="FailWith"/> makes the store throw on every operation, so
/// the "prefs unavailable → fail safe" branches that the production services
/// are careful to write can actually be exercised. On a real device those
/// branches fire only on a broken install, which is precisely why they need a
/// test rather than a hope.</para>
/// </summary>
public sealed class FakePreferences : IPreferences
{
	private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

	/// <summary>When set, every member throws this exception.</summary>
	public Exception? FailWith { get; set; }

	/// <summary>Number of <see cref="Set{T}"/> calls, for asserting write-through behaviour.</summary>
	public int SetCount { get; private set; }

	/// <summary>Seeds a value directly, bypassing the failure switch and the write counter.</summary>
	public void Seed(string key, object? value) => _values[key] = value;

	public bool ContainsKey(string key, string? sharedName = null)
	{
		Throw();
		return _values.ContainsKey(key);
	}

	public void Remove(string key, string? sharedName = null)
	{
		Throw();
		_values.Remove(key);
	}

	public void Clear(string? sharedName = null)
	{
		Throw();
		_values.Clear();
	}

	public void Set<T>(string key, T value, string? sharedName = null)
	{
		Throw();
		SetCount++;
		_values[key] = value;
	}

	public T Get<T>(string key, T defaultValue, string? sharedName = null)
	{
		Throw();
		return _values.TryGetValue(key, out var stored) && stored is T typed ? typed : defaultValue;
	}

	private void Throw()
	{
		if (FailWith is not null) throw FailWith;
	}
}
