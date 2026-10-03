using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UnityModels = TheBleedingDeacons.Unity.Models;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Support;

/// <summary>
/// A scripted Unity — the Integrity REST API a tablet syncs from and
/// reconciles to — standing behind the real <c>UnityRestSharp</c>.
///
/// <para><b>Under the real client, not instead of it.</b> The client is
/// handed an <see cref="HttpClient"/> over this handler, so every request a
/// scenario provokes goes through the same URL building, envelope parsing
/// and error mapping a tablet's does. What it serves is integrity-sharp's
/// own model types, serialised with the client's own options, so the wire
/// shape cannot drift from what the client reads.</para>
///
/// <para><b>It honours paging the way the server does.</b> A double that
/// answered everything in one page could never show a sync that stops
/// after the first.</para>
///
/// <para><b>Refusals are 4xx.</b> The client retries 5xx, 408 and 429 with
/// backoff, which is right for a tablet and would make every refusal
/// scenario take seconds. A 400, 409 or 422 comes straight back.</para>
/// </summary>
public sealed partial class FakeUnity : HttpMessageHandler
{
	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public List<UnityModels.Group> Groups { get; } = [];

	public List<UnityModels.Position> Positions { get; } = [];

	public List<UnityModels.Member> Members { get; } = [];

	public List<UnityModels.IntergroupMeeting> IntergroupMeetings { get; } = [];

	/// <summary>Every request, in order: method, path and (for a POST) the body.</summary>
	public List<Call> Calls { get; } = [];

	/// <summary>Route suffixes to refuse, with the status and error code to refuse them with.</summary>
	public Dictionary<string, (HttpStatusCode Status, string Code)> Refusals { get; } = new(StringComparer.Ordinal);

	/// <summary>A page number to refuse on a listing, to break a sync part-way.</summary>
	public int? RefusePage { get; set; }

	/// <summary>The id the next created member is given.</summary>
	public int NextMemberId { get; set; } = 900;

	public int PageSize { get; set; } = 500;

	public IEnumerable<Call> Posts(string suffix) =>
		Calls.Where(c => c.Method == "POST" && c.Path.EndsWith(suffix, StringComparison.Ordinal));

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;
		var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
		var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
		var call = new Call(request.Method.Method, path, body is null ? null : JsonDocument.Parse(body).RootElement.Clone());
		Calls.Add(call);

		var refusal = Refusals.FirstOrDefault(r => path.EndsWith(r.Key, StringComparison.Ordinal));
		if (refusal.Key is not null)
		{
			return Error(refusal.Value.Status, refusal.Value.Code);
		}

		var route = path.Replace("/wp-json/integrity/v1/", string.Empty, StringComparison.Ordinal);
		var page = int.TryParse(query["page"], out var p) ? p : 1;

		if (request.Method == HttpMethod.Get)
		{
			return route switch
			{
				"groups" => Page(Groups, page),
				"positions" => Page(Positions, page),
				"members" => Page(Members, page),
				"intergroup-meetings" => Page(IntergroupMeetings, page),
				_ => Error(HttpStatusCode.NotFound, "rest_no_route"),
			};
		}

		if (route == "members/create")
		{
			var created = JsonSerializer.Deserialize<UnityModels.CreateMemberRequest>(body!, Json)!;
			var member = new UnityModels.Member { Id = NextMemberId++, AnonymousName = created.AnonymousName };
			Members.Add(member);
			return Ok(member);
		}

		var memberRoute = MemberRoute().Match(route);
		if (memberRoute.Success)
		{
			var id = int.Parse(memberRoute.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
			return Ok(Members.FirstOrDefault(m => m.Id == id) ?? new UnityModels.Member { Id = id });
		}

		if (MeetingRoute().IsMatch(route))
		{
			return Ok(new { });
		}

		return Error(HttpStatusCode.NotFound, "rest_no_route");
	}

	private HttpResponseMessage Page<T>(List<T> all, int page)
	{
		if (page == RefusePage)
		{
			return Error(HttpStatusCode.BadRequest, "page_refused");
		}

		var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
		var slice = all.Skip((page - 1) * PageSize).Take(PageSize).ToList();
		return Respond(HttpStatusCode.OK, new
		{
			success = true,
			data = slice,
			meta = new { total = all.Count, page, per_page = PageSize, total_pages = totalPages },
		});
	}

	private static HttpResponseMessage Ok(object data) =>
		Respond(HttpStatusCode.OK, new { success = true, data });

	private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
		Respond(status, new { success = false, error = new { code, message = $"Refused: {code}" } });

	private static HttpResponseMessage Respond(HttpStatusCode status, object payload) =>
		new(status) { Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json") };

	[GeneratedRegex(@"^members/(?<id>-?\d+)/(update|compliance)$")]
	private static partial Regex MemberRoute();

	[GeneratedRegex(@"^intergroup-meetings/\d+/(register|unregister)-(group|officer)$")]
	private static partial Regex MeetingRoute();

	/// <summary>One request the tablet made.</summary>
	public sealed record Call(string Method, string Path, JsonElement? Body)
	{
		public string? Str(string property) =>
			Body is { } b && b.TryGetProperty(property, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

		public int? Int(string property) =>
			Body is { } b && b.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

		public bool? Bool(string property) =>
			Body is { } b && b.TryGetProperty(property, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

		public bool Has(string property) =>
			Body is { } b && b.TryGetProperty(property, out _);
	}
}
