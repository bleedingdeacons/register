namespace TheBleedingDeacons.Intergroup.Register.Exceptions;

/// <summary>
/// Thrown instead of sending the SMTP password over a connection that is not
/// encrypted: TLS switched off in Freedom (<c>smtp.enable_ssl</c>), or a
/// server that gave no encrypted connection however it was asked.
///
/// <para>A TLS <see cref="System.Security.Authentication.AuthenticationException"/>
/// on purpose, so everything that already handles a TLS failure handles this
/// one the same way: not retried, counted by the circuit breaker, and named
/// as a TLS problem by the reachability probe and the Test button. It is a
/// configuration problem, and retrying would only refuse again.</para>
/// </summary>
public sealed class SmtpNotEncryptedException : System.Security.Authentication.AuthenticationException
{
	public SmtpNotEncryptedException(string host, bool tlsSwitchedOff)
		: base(tlsSwitchedOff
			? $"TLS is switched off for {host} (smtp.enable_ssl), so the SMTP password was not sent. Switch it on in Freedom."
			: $"The connection to {host} is not encrypted, so the SMTP password was not sent.")
	{
		Host = host;
		TlsSwitchedOff = tlsSwitchedOff;
	}

	/// <summary>The SMTP server that was not encrypted.</summary>
	public string Host { get; }

	/// <summary>True when TLS was switched off in the settings, rather than missing at the server.</summary>
	public bool TlsSwitchedOff { get; }
}
