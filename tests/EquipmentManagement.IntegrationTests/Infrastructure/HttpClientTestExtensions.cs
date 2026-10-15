using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquipmentManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only helpers for the antiforgery double-submit flow (ADR-004) and for
/// deserializing camelCase API responses. Production code never needs this — the
/// real frontend does the equivalent in TypeScript.
/// </summary>
public static class HttpClientTestExtensions
{
    /// <summary>
    /// Matches ASP.NET Core MVC's own JSON options (camelCase, case-insensitive,
    /// string enums — see Program.cs's AddJsonOptions for why Phase 2's
    /// WorkforceType/EngagementType/AllocationStatus are serialized as names).
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<string> GetAntiforgeryTokenAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/antiforgery/token");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    public static Task<HttpResponseMessage> PostWithCsrfAsync<T>(this HttpClient client, string url, T body, string csrfToken) =>
        SendWithCsrfAsync(client, HttpMethod.Post, url, body, csrfToken);

    public static Task<HttpResponseMessage> PutWithCsrfAsync<T>(this HttpClient client, string url, T body, string csrfToken) =>
        SendWithCsrfAsync(client, HttpMethod.Put, url, body, csrfToken);

    public static Task<HttpResponseMessage> DeleteWithCsrfAsync(this HttpClient client, string url, string csrfToken) =>
        SendWithCsrfAsync<object?>(client, HttpMethod.Delete, url, null, csrfToken);

    private static Task<HttpResponseMessage> SendWithCsrfAsync<T>(HttpClient client, HttpMethod method, string url, T body, string csrfToken)
    {
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        var csrfToken = await client.GetAntiforgeryTokenAsync();
        var response = await client.PostWithCsrfAsync("/api/auth/login", new { email, password }, csrfToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> PostFileWithCsrfAsync(
        this HttpClient client, string url, string fieldName, byte[] fileBytes, string fileName, string contentType, string csrfToken,
        IReadOnlyDictionary<string, string>? formFields = null)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, fieldName, fileName);

        if (formFields is not null)
        {
            foreach (var (key, value) in formFields)
            {
                content.Add(new StringContent(value), key);
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Like <see cref="PostFileWithCsrfAsync"/> but for endpoints that accept
    /// zero or more files under the same repeated field name (e.g. the exactly-3
    /// condition photos on equipment creation/return receipt) alongside plain
    /// form fields.
    /// </summary>
    public static async Task<HttpResponseMessage> PostMultipartWithCsrfAsync(
        this HttpClient client, string url, string fileFieldName, IReadOnlyList<(byte[] Bytes, string FileName, string ContentType)> files,
        IReadOnlyDictionary<string, string> formFields, string csrfToken)
    {
        using var content = new MultipartFormDataContent();

        foreach (var (key, value) in formFields)
        {
            content.Add(new StringContent(value), key);
        }

        foreach (var file in files)
        {
            var fileContent = new ByteArrayContent(file.Bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, fileFieldName, file.FileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return await client.SendAsync(request);
    }
}
