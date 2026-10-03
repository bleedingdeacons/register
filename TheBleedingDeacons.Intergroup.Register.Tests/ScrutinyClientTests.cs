using System.Net;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>Scrutiny's privacy-policy routes, answering things a client has to cope with.</summary>
public sealed class ScrutinyClientTests
{
	private readonly FakeConfigurationService _config = new();

	private ScrutinyClient Client(StubHttpHandler handler) => new(new HttpClient(handler), _config);

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task NoBaseUrlFailsLoudlyBeforeAnyRequest(string baseUrl)
	{
		_config.Unity.BaseUrl = baseUrl;
		var handler = new StubHttpHandler();

		await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).GetActivePrivacyPolicyAsync());
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task ATrailingSlashOnTheSiteDoesNotDoubleUp()
	{
		_config.Unity.BaseUrl = "https://aa-bristol.org/";
		var handler = StubHttpHandler.Answering(HttpStatusCode.NotFound, string.Empty);

		await Client(handler).GetActivePrivacyPolicyAsync();

		Assert.Equal("https://aa-bristol.org/wp-json/scrutiny/v1/privacy-policies/active", Assert.Single(handler.Requests).ToString());
	}

	[Fact]
	public async Task TheActiveFilterIsLowerCaseBecauseWordPressIsCaseSensitive()
	{
		var handler = new StubHttpHandler();

		await Client(handler).GetPrivacyPoliciesAsync(activeOnly: true);

		Assert.EndsWith("?active=true", Assert.Single(handler.Requests).ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task NoActivePolicyIsNullNotAnError() =>
		Assert.Null(await Client(StubHttpHandler.Answering(HttpStatusCode.NotFound, "{\"code\":\"not_found\"}")).GetActivePrivacyPolicyAsync());

	[Fact]
	public async Task AnUnknownPolicyIsNullNotAnError() =>
		Assert.Null(await Client(StubHttpHandler.Answering(HttpStatusCode.NotFound, string.Empty)).GetPrivacyPolicyAsync(7));

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.InternalServerError)]
	[InlineData(HttpStatusCode.BadGateway)]
	public async Task AnyOtherFailureThrows(HttpStatusCode status)
	{
		var client = Client(StubHttpHandler.Answering(status, "{}"));

		await Assert.ThrowsAsync<HttpRequestException>(() => client.GetActivePrivacyPolicyAsync());
		await Assert.ThrowsAsync<HttpRequestException>(() => client.GetPrivacyPoliciesAsync());
	}

	[Fact]
	public async Task AnHtmlPageWhereJsonShouldBeThrows()
	{
		// What a misconfigured site or a captive portal actually answers.
		var client = Client(StubHttpHandler.Answering(HttpStatusCode.OK, "<html><body>Log in to Wi-Fi</body></html>", "text/html"));

		await Assert.ThrowsAnyAsync<Exception>(() => client.GetActivePrivacyPolicyAsync());
	}

	[Fact]
	public async Task MalformedJsonThrows() =>
		await Assert.ThrowsAsync<JsonException>(() =>
			Client(StubHttpHandler.Answering(HttpStatusCode.OK, "{\"id\": 1, \"title\":")).GetActivePrivacyPolicyAsync());

	[Fact]
	public async Task ALiteralNullListIsAnEmptyList() =>
		Assert.Empty(await Client(StubHttpHandler.Answering(HttpStatusCode.OK, "null")).GetPrivacyPoliciesAsync());

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task APolicyIdMustBePositive(int id) =>
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client(new StubHttpHandler()).GetPrivacyPolicyAsync(id));

	[Fact]
	public void MissingCollaboratorsAreRefused()
	{
		Assert.Throws<ArgumentNullException>(() => new ScrutinyClient(null!, _config));
		Assert.Throws<ArgumentNullException>(() => new ScrutinyClient(new HttpClient(), null!));
	}
}
