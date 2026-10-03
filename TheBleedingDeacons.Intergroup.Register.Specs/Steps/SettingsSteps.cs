using System.Globalization;
using Microsoft.Extensions.Configuration;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>The Freedom overlay and the Better Stack guard.</summary>
[Binding]
public sealed class SettingsSteps
{
	private readonly SmtpConfiguration _tablet = new() { Host = "smtp.tablet.example", Port = 587, EnableSsl = true };
	private readonly Dictionary<string, string> _managed = new(StringComparer.Ordinal);
	private readonly RecordingLogShipper _shipper = new();
	private bool _optionsRead;
	private object? _options;

	[Given(@"^the tablet's own SMTP (host|port|TLS) is ""([^""]*)""$")]
	public void TabletOwn(string setting, string value)
	{
		switch (setting)
		{
			case "host":
				_tablet.Host = value;
				break;
			case "port":
				_tablet.Port = int.Parse(value, CultureInfo.InvariantCulture);
				break;
			default:
				_tablet.EnableSsl = bool.Parse(value);
				break;
		}
	}

	[Given(@"^Freedom holds ""([^""]*)"" for ""([^""]*)""$")]
	public void FreedomHolds(string value, string key) => _managed[key] = value;

	[Given(@"^Freedom holds nothing$")]
	public void FreedomHoldsNothing() => _managed.Clear();

	[Then(@"^the SMTP (host|port|TLS) in use is ""([^""]*)""$")]
	public void InUse(string setting, string expected)
	{
		var config = FreedomSettings.Apply(_tablet, key => _managed.GetValueOrDefault(key));

		var actual = setting switch
		{
			"host" => config.Host,
			"port" => config.Port.ToString(CultureInfo.InvariantCulture),
			_ => config.EnableSsl ? "true" : "false",
		};

		actual.ShouldBe(expected);
	}

	[Given(@"^a build that names no Freedom site$")]
	public void NoSite()
	{
		_options = FreedomSettings.OptionsFrom(new ConfigurationBuilder().Build());
		_optionsRead = true;
	}

	[Then(@"^Freedom is off$")]
	public void Off()
	{
		_optionsRead.ShouldBeTrue();
		_options.ShouldBeNull();
	}

	[Given(@"^a Better Stack endpoint of ""([^""]*)""$")]
	public void Endpoint(string endpoint) =>
		_shipper.Ship(new BetterStackConfiguration { Endpoint = endpoint, SourceToken = "source-token" });

	[Then(@"^logs (are|are not) shipped$")]
	public void Shipped(string verdict)
	{
		var configured = _shipper.Reconfigured.ShouldHaveSingleItem();
		if (verdict == "are")
		{
			configured.ShouldNotBeNull();
		}
		else
		{
			configured.ShouldBeNull();
		}
	}
}
