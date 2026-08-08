using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests.Fakes;

/// <summary>
/// <see cref="IFileSystem"/> rooted at a throwaway directory. Implements
/// <see cref="IDisposable"/> so a test that writes settings files cleans up
/// after itself; give one instance per test so they cannot see each other's
/// files.
/// </summary>
public sealed class TempFileSystem : IFileSystem, IDisposable
{
	public TempFileSystem()
	{
		AppDataDirectory = Path.Combine(Path.GetTempPath(), "register-tests", Guid.NewGuid().ToString("N"));
		CacheDirectory = Path.Combine(AppDataDirectory, "cache");
		Directory.CreateDirectory(AppDataDirectory);
		Directory.CreateDirectory(CacheDirectory);
	}

	public string AppDataDirectory { get; }

	public string CacheDirectory { get; }

	public Task<Stream> OpenAppPackageFileAsync(string filename) =>
		throw new NotSupportedException("Tests do not read app-package files.");

	public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(AppDataDirectory))
				Directory.Delete(AppDataDirectory, recursive: true);
		}
		catch (IOException)
		{
			// A leftover temp directory is not worth failing a test over.
		}
	}
}

/// <summary>
/// In-memory <see cref="ISecureStorage"/>. As with <see cref="FakePreferences"/>,
/// <see cref="FailWith"/> exercises the "secure storage unavailable" branches,
/// which on a device only fire when the platform keystore is broken.
/// </summary>
public sealed class FakeSecureStorage : ISecureStorage
{
	private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

	public Exception? FailWith { get; set; }

	public Task<string?> GetAsync(string key)
	{
		if (FailWith is not null) throw FailWith;
		return Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
	}

	public Task SetAsync(string key, string value)
	{
		if (FailWith is not null) throw FailWith;
		_values[key] = value;
		return Task.CompletedTask;
	}

	public bool Remove(string key) => _values.Remove(key);

	public void RemoveAll() => _values.Clear();
}

/// <summary>
/// <see cref="IDeviceInfo"/> with settable values, so the device-label
/// defaulting rules can be driven across platforms from a single machine.
/// </summary>
public sealed class FakeDeviceInfo : IDeviceInfo
{
	public string Model { get; set; } = "Pixel 8";

	public string Manufacturer { get; set; } = "Google";

	public string Name { get; set; } = "Test Device";

	public string VersionString { get; set; } = "15";

	public Version Version { get; } = new(15, 0);

	public DevicePlatform Platform { get; set; } = DevicePlatform.Android;

	public DeviceIdiom Idiom { get; set; } = DeviceIdiom.Phone;

	public DeviceType DeviceType { get; set; } = DeviceType.Physical;
}
