using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>
/// The outgoing mail queue: what is waiting in it, and what happens when it
/// is worked through. "Sent" in a feature means queued unless it says the
/// queue ran — that is the distinction the app draws too.
/// </summary>
[Binding]
public sealed class EmailSteps(World world)
{
	private bool? _queueRan;

	[Then(@"^""([^""]*)"" is sent a welcome for ""([^""]*)""$")]
	public async Task Welcomed(string address, string name)
	{
		var emails = await To(address);

		emails.Count(e => e.Subject == $"Registered: {name}").ShouldBe(1, $"one welcome to {address}");
	}

	[Then(@"^(\d+) emails? (?:is|are) waiting to be sent$")]
	public async Task Waiting(int count) => (await world.Email.GetQueuedEmailsAsync()).Count.ShouldBe(count);

	[Then(@"^nobody is emailed$")]
	public async Task Nobody() => (await world.Email.GetQueuedEmailsAsync()).ShouldBeEmpty();

	[Given(@"^the email queue has been emptied$")]
	public Task Emptied() => world.Email.ClearQueueAsync();

	[Then(@"^""([^""]*)"" is sent a copy of version ""([^""]*)""$")]
	public async Task CopySent(string address, string version)
	{
		var email = (await To(address)).ShouldHaveSingleItem();

		email.Subject.ShouldBe($"Privacy policy acceptance: {world.Policy.Cached!.Title} (v{version})");
		email.IsHtml.ShouldBeTrue();
	}

	[Then(@"^replies to it go to ""([^""]*)""$")]
	public async Task RepliesTo(string address) =>
		(await world.Email.GetQueuedEmailsAsync()).ShouldHaveSingleItem().ReplyTo.ShouldBe(address);

	[When(@"^the queue runs$")]
	public async Task QueueRuns() => _queueRan = await world.Email.ProcessQueueAsync();

	[Then(@"^the queue run reported that it did not run$")]
	public void DidNotRun() => _queueRan.ShouldBe(false);

	// The background timer's tick, run directly: the timer's first run is a
	// minute away, and the circuit breaker lives only in its callback.
	[When(@"^the queue runs in the background (\d+) times?$")]
	public async Task RunsInBackground(int times)
	{
		for (var i = 0; i < times; i++)
		{
			await world.Email.ProcessQueueInBackground();
		}
	}

	[Then(@"^background sending is paused$")]
	public void Paused() => world.Email.IsCircuitOpen.ShouldBeTrue();

	[Then(@"^background sending is not paused$")]
	public void NotPaused()
	{
		world.Email.IsCircuitOpen.ShouldBeFalse();
		world.Email.ConsecutiveQueueFailures.ShouldBe(0, "no run counted as a failure");
	}

	[Then(@"^the email to ""([^""]*)"" has been sent$")]
	public async Task HasBeenSent(string address)
	{
		(await To(address)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Sent);
		world.Smtp.Delivered.ShouldHaveSingleItem().To.ToString().ShouldContain(address);
	}

	[Then(@"^the email to ""([^""]*)"" is waiting$")]
	public async Task IsWaiting(string address) =>
		(await To(address)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Pending);

	[Then(@"^the email to ""([^""]*)"" is waiting, after (\d+) attempts?$")]
	public async Task IsWaitingAfter(string address, int attempts)
	{
		var email = (await To(address)).ShouldHaveSingleItem();
		email.Status.ShouldBe(EmailStatus.Pending);
		email.AttemptCount.ShouldBe(attempts);
	}

	[Then(@"^the email to ""([^""]*)"" has failed$")]
	public async Task HasFailed(string address) =>
		(await To(address)).ShouldHaveSingleItem().Status.ShouldBe(EmailStatus.Failed);

	[Then(@"^nothing reached the mail server$")]
	public void NothingReached()
	{
		world.Smtp.Connections.ShouldBe(0);
		world.Smtp.Delivered.ShouldBeEmpty();
	}

	[Given(@"^the mail server refuses the password$")]
	public void RefusesPassword() =>
		world.Smtp.RefusePassword = new MailKit.Security.AuthenticationException("535 5.7.8 Authentication credentials invalid");

	[Given(@"^the mail server cannot be reached$")]
	public void Unreachable() =>
		world.Smtp.RefuseConnections = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostUnreachable);

	[Given(@"^the mail server refuses every message$")]
	public void RefusesMessages() => world.Smtp.RefuseMessages = new IOException("Connection reset by peer");

	[Given(@"^the email to ""([^""]*)"" has already failed twice$")]
	public async Task FailedTwice(string address)
	{
		await using var db = world.Mail.CreateDbContext();
		var email = await db.QueuedEmails.SingleAsync(e => e.To == address);
		email.AttemptCount = 2;
		await db.SaveChangesAsync();
	}

	private async Task<List<QueuedEmail>> To(string address) =>
		(await world.Email.GetQueuedEmailsAsync()).Where(e => e.To == address).ToList();
}
