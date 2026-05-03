namespace TheBleedingDeacons.Intergroup.Register.Exceptions
{
	/// <summary>
	/// Thrown by the sync stage when Scrutiny reports that no privacy
	/// policy is currently flagged active on the upstream WordPress site.
	///
	/// <para>This is the gate that prevents a meeting from starting
	/// against an empty policy. The sync stage clears the on-device
	/// privacy-policy cache and then throws this exception; the calling
	/// view-model's existing sync-failed catch (see
	/// <c>AdminViewModel.LoadUnity</c>) surfaces the message to the
	/// operator and leaves the meeting in <c>NotStarted</c> state.</para>
	///
	/// <para>The exception message is intentionally aimed at the operator
	/// — it states what's wrong on the server and what they need to do
	/// to fix it — because the existing sync-failed UI displays the
	/// message verbatim.</para>
	/// </summary>
	public sealed class NoActivePrivacyPolicyException : Exception
	{
		// Message kept here as a constant so tests can assert against
		// it without scraping the catch-block UI.
		public const string DefaultMessage =
			"No active privacy policy is published on the Unity site. " +
			"Publish or activate a policy in the Scrutiny admin before starting a meeting.";

		public NoActivePrivacyPolicyException()
			: base(DefaultMessage)
		{
		}

		public NoActivePrivacyPolicyException(string message)
			: base(message)
		{
		}

		public NoActivePrivacyPolicyException(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
