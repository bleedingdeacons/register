using System.Net;

namespace TheBleedingDeacons.Unity.Intergroup.Tests;

/// <summary>
/// A stub <see cref="HttpMessageHandler"/> that returns a JSON body chosen by a
/// caller-supplied responder, so a real <c>UnityRestSharp</c> can be driven from
/// the sync tests without a live server.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<Uri, (HttpStatusCode Status, string Body)> responder)
	: HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var (status, body) = responder(request.RequestUri!);

		var response = new HttpResponseMessage(status)
		{
			Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
			RequestMessage = request,
		};

		return Task.FromResult(response);
	}
}
