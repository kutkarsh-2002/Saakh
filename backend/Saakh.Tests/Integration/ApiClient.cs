using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Saakh.Api.Domain;
using Saakh.Api.Dtos;

namespace Saakh.Tests.Integration;

/// <summary>
/// A thin wrapper over the test host's HttpClient. Tests read as the product
/// reads — sign up, send interest, raise a ticket — rather than as a sequence of
/// URLs and status codes.
/// </summary>
public class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    public SessionDto? Session { get; set; }

    public Guid ProfileId => Session?.Profile?.Id ?? throw new InvalidOperationException("No profile.");

    public void Authenticate(string accessToken) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public async Task<(HttpStatusCode Status, T? Body)> PostAsync<T>(string path, object? payload)
    {
        var response = await _http.PostAsJsonAsync(path, payload ?? new { }, Json);
        return (response.StatusCode, await ReadAsync<T>(response));
    }

    /// <summary>
    /// For the arrange steps, where a non-success status is a broken test rather
    /// than the thing under test. Without this a failed setup call surfaces later
    /// as a NullReferenceException on the returned body, which says nothing about
    /// what actually went wrong.
    /// </summary>
    public async Task<T> PostOkAsync<T>(string path, object? payload)
    {
        var response = await _http.PostAsJsonAsync(path, payload ?? new { }, Json);
        return await RequireAsync<T>(response, "POST", path);
    }

    public async Task<T> GetOkAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        return await RequireAsync<T>(response, "GET", path);
    }

    public async Task<T> PutOkAsync<T>(string path, object payload)
    {
        var response = await _http.PutAsJsonAsync(path, payload, Json);
        return await RequireAsync<T>(response, "PUT", path);
    }

    private static async Task<T> RequireAsync<T>(HttpResponseMessage response, string verb, string path)
    {
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{verb} {path} returned {(int)response.StatusCode} {response.StatusCode}. Body: {raw}");
        }

        var body = string.IsNullOrWhiteSpace(raw)
            ? default
            : JsonSerializer.Deserialize<T>(raw, Json);

        return body ?? throw new InvalidOperationException(
            $"{verb} {path} succeeded but returned no {typeof(T).Name}. Body: {raw}");
    }

    public async Task<HttpStatusCode> PostStatusAsync(string path, object? payload)
    {
        var response = await _http.PostAsJsonAsync(path, payload ?? new { }, Json);
        return response.StatusCode;
    }

    public async Task<(HttpStatusCode Status, T? Body)> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        return (response.StatusCode, await ReadAsync<T>(response));
    }

    public async Task<(HttpStatusCode Status, T? Body)> PutAsync<T>(string path, object payload)
    {
        var response = await _http.PutAsJsonAsync(path, payload, Json);
        return (response.StatusCode, await ReadAsync<T>(response));
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch (JsonException)
        {
            // An error envelope where a success shape was expected: the caller is
            // asserting on the status code in that case.
            return default;
        }
    }

    /// <summary>
    /// Registers a trader end to end. Returns a client already carrying that
    /// account's access token.
    /// </summary>
    public static async Task<ApiClient> RegisterAsync(
        SaakhApiFactory factory,
        ProfileRole role,
        string name,
        string? gstin = null,
        string state = "Maharashtra",
        string district = "Pune",
        DealCategory category = DealCategory.RawMaterial,
        int? subTypeId = 10)
    {
        var client = new ApiClient(factory.CreateClient());
        var phone = RandomPhone();
        var email = $"{Guid.NewGuid():N}@saakh.test";

        var (status, auth) = await client.PostAsync<AuthResultDto>("/api/auth/register", new
        {
            email,
            password = "Test@Password1",
            fullName = name,
            role,
            phone,
            gstin,
            isBusiness = gstin is not null,
            businessSize = gstin is not null ? BusinessSize.Small : BusinessSize.Individual,
            country = "India",
            state,
            district,
            category,
            categorySubTypeId = subTypeId,
            capacityMin = 1000,
            capacityMax = 50000,
            capacityUnit = "kg",
            ownTradeDescription = role == ProfileRole.Seeker ? "Kirana store" : null
        });

        if (status != HttpStatusCode.OK || auth is null)
        {
            throw new InvalidOperationException($"Registration failed with {status} for {email}.");
        }

        client.Authenticate(auth.AccessToken);
        client.Session = auth.Session;
        return client;
    }

    /// <summary>A GSTIN the mock verifier accepts, with a per-call unique body.</summary>
    public static string ValidGstin()
    {
        var letters = new string(Enumerable.Range(0, 5)
            .Select(_ => (char)Random.Shared.Next('A', 'Z' + 1)).ToArray());

        return $"27{letters}{Random.Shared.Next(1000, 9999)}F1Z{(char)Random.Shared.Next('A', 'Z' + 1)}";
    }

    /// <summary>
    /// The two-party path to an Open deal: one side proposes terms, the other agrees
    /// them. There is no single-party route, so every test that needs a live deal goes
    /// through this.
    /// </summary>
    public static async Task<DealRowDto> OpenDealAsync(ApiClient proposer, ApiClient accepter,
        Guid interestId, object? terms = null)
    {
        var body = terms ?? new
        {
            interestId,
            category = DealCategory.RawMaterial,
            categorySubTypeId = 10,
            capacity = 500,
            capacityUnit = "kg",
            materialDescription = "Nagpur oranges, grade A",
            description = "Weekly supply on 30-day credit.",
            estimatedSettlementTime = DateTimeOffset.UtcNow.AddDays(30)
        };

        var (proposeStatus, proposal) = await proposer.PostAsync<DealProposalDto>(
            "/api/deals/proposals", body);

        if (proposeStatus != HttpStatusCode.OK || proposal is null)
        {
            throw new InvalidOperationException($"Proposing terms failed with {proposeStatus}.");
        }

        var (acceptStatus, deal) = await accepter.PostAsync<DealRowDto>(
            $"/api/deals/proposals/{proposal.Id}/accept", new { });

        if (acceptStatus != HttpStatusCode.OK || deal is null)
        {
            throw new InvalidOperationException($"Agreeing terms failed with {acceptStatus}.");
        }

        return deal;
    }

    private static string RandomPhone() => $"9{Random.Shared.NextInt64(100000000, 999999999)}";
}
