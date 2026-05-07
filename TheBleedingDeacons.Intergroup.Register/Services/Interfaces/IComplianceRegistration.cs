using System;
using System.Threading;
using System.Threading.Tasks;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	/// <summary>
	/// Records a member's GDPR acceptance or revocation locally.
	///
	/// Mirrors the shape of <see cref="IAttendanceRegistration{T}"/> but is
	/// a non-generic interface because compliance only ever applies to one
	/// entity kind (Member), and the call signatures need extra parameters
	/// (policy version, statement, timestamp) that don't fit a single
	/// <c>Register(T)</c> method shape.
	///
	/// Implementations must work fully offline — see
	/// <see cref="Services.ComplianceService"/> for the durability model.
	/// </summary>
	public interface IComplianceRegistration
	{
		/// <summary>
		/// Records that a member has accepted the privacy policy.
		/// </summary>
		/// <param name="member">The member who accepted. Must already exist in the local DB.</param>
		/// <param name="version">The privacy policy version that was accepted (e.g. <c>"2.1"</c>).</param>
		/// <param name="statement">
		/// Historically the exact statement the member accepted. Retained
		/// in the signature for ABI continuity, but no longer used by
		/// <see cref="Services.ComplianceService"/>: the wording persisted
		/// against the acceptance and surfaced in the confirmation email
		/// is now sourced from the cached active policy
		/// (<see cref="Models.CachedPrivacyPolicy.Policy"/>) so the DB,
		/// the durability log, and the email all carry the same
		/// upstream-authoritative wording. Pass any value (including
		/// <c>string.Empty</c>); it is ignored.
		/// </param>
		/// <param name="method">
		/// How acceptance was captured. Defaults to <c>"register-app"</c>;
		/// callers can override (e.g. <c>"web-form"</c>, <c>"import"</c>).
		/// Sent verbatim to the Unity server during reconciliation.
		/// </param>
		/// <param name="acceptedAtUtc">
		/// Timestamp of the acceptance in UTC. Defaults to <see cref="DateTime.UtcNow"/>.
		/// Callers backfilling historical records may pass an earlier value.
		/// </param>
		/// <param name="ct">Cancellation token.</param>
		Task RecordAcceptance(
			Member member,
			string version,
			string statement,
			string method = "register-app",
			DateTime? acceptedAtUtc = null,
			CancellationToken ct = default);

		/// <summary>
		/// Records that a member has revoked their previously-given consent.
		/// </summary>
		/// <param name="member">The member who revoked. Must already exist in the local DB.</param>
		/// <param name="revokedAtUtc">
		/// Timestamp of the revocation in UTC. Defaults to <see cref="DateTime.UtcNow"/>.
		/// </param>
		/// <param name="ct">Cancellation token.</param>
		Task RecordRevocation(
			Member member,
			DateTime? revokedAtUtc = null,
			CancellationToken ct = default);
	}
}
