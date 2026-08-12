using System.ComponentModel.DataAnnotations;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Email format validation used by the GSR and officer edit screens.
///
/// <para><b>Why this exists:</b></para>
/// <see cref="EmailAddressAttribute"/> alone is too permissive — it accepts
/// <c>name@host</c> with no TLD (e.g. <c>thorn@thorn</c>), but the Unity
/// (WordPress) server rejects such addresses with HTTP 400 at member-create
/// time. That failure surfaces only during background reconciliation, long
/// after the user has left the edit screen, and (because reconciliation
/// continues past the warning) it leaves a temporary member ID stuck in the
/// local database that can never be promoted to a real Unity ID. The next
/// sync sends the temp ID into <c>register-group</c> and gets another 400.
///
/// To keep the rejection at the input boundary where the user can fix it,
/// we tighten the client check to require a domain with at least one dot
/// and a non-empty TLD — matching WordPress's <c>is_email()</c> rule.
///
/// <para><b>Behaviour:</b></para>
/// <see cref="IsValid"/> returns <c>true</c> only when the value:
/// passes <see cref="EmailAddressAttribute"/> (basic shape and characters);
/// contains exactly one <c>@</c>; has a non-empty local part; has a domain
/// part containing at least one <c>.</c> with non-empty labels on both sides
/// of the final dot.
///
/// Empty / whitespace input returns <c>false</c>. Required-ness is the
/// caller's concern — this method only validates format.
///
/// <para><b>Service addresses:</b></para>
/// <see cref="IsIntergroupAddress"/> is a separate policy check (not part
/// of <see cref="IsValid"/>, which stays format-only) for the personal
/// email fields on the GSR and officer edit screens.
/// </summary>
public static class EmailValidator
{
	/// <summary>
	/// The intergroup's own domain. Addresses on it are service aliases
	/// (<see cref="TheBleedingDeacons.Unity.Intergroup.Entities.Member.Email"/>), never a
	/// member's personal address.
	/// </summary>
	private const string IntergroupDomain = "aa-bristol.org";

	/// <summary>
	/// Returns <c>true</c> when <paramref name="email"/> mentions the
	/// intergroup domain anywhere in the value.
	///
	/// A member's personal email is the address they own themselves; the
	/// aa-bristol.org address is the rotating service alias that forwards
	/// *to* it. Storing the alias as the personal address breaks that
	/// forward on the next rotation and means the member can no longer be
	/// reached once they leave the position.
	///
	/// The check is a plain case-insensitive substring match rather than a
	/// domain comparison, so it also catches sub-domains and look-alikes
	/// such as <c>gsr@mail.aa-bristol.org</c> or <c>…@aa-bristol.org.uk</c>.
	/// </summary>
	public static bool IsIntergroupAddress(string? email) =>
		email is not null &&
		email.Contains(IntergroupDomain, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Returns <c>true</c> when <paramref name="email"/> is a syntactically
	/// valid address with a TLD. Does not perform DNS or mailbox checks.
	/// </summary>
	public static bool IsValid(string? email)
	{
		if (string.IsNullOrWhiteSpace(email)) return false;

		var trimmed = email.Trim();

		// Basic shape & character set first. Wrapped in try/catch because
		// EmailAddressAttribute has been seen to throw on pathological
		// inputs on some platforms (see SettingsViewModel.IsValidEmail
		// for the original note that motivated the guard).
		try
		{
			if (!new EmailAddressAttribute().IsValid(trimmed)) return false;
		}
		catch
		{
			return false;
		}

		// Reject anything without a domain TLD. WordPress's is_email()
		// requires a dot in the domain and non-empty labels on each side
		// of the final dot, so we mirror that here to keep client and
		// server in agreement.
		var atIndex = trimmed.LastIndexOf('@');
		if (atIndex <= 0 || atIndex == trimmed.Length - 1) return false;

		var local = trimmed[..atIndex];
		var domain = trimmed[(atIndex + 1)..];

		if (local.Length == 0 || domain.Length == 0) return false;

		var lastDot = domain.LastIndexOf('.');
		if (lastDot <= 0 || lastDot == domain.Length - 1) return false;

		// Both sides of the final dot must be non-empty (we've checked
		// `lastDot > 0` and `lastDot < domain.Length - 1`, so this is
		// already guaranteed — kept explicit for readability).
		var tld = domain[(lastDot + 1)..];
		if (tld.Length == 0) return false;

		return true;
	}
}
