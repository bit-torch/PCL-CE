using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Kernel.Accounts;

internal sealed record JsonHttpResponse(HttpStatusCode Status, JsonNode? Body, string Raw)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public string ErrorMessage
    {
        get
        {
            if (Body is JsonObject json)
            {
                foreach (var key in new[] { "error_description", "errorMessage", "message", "error" })
                {
                    if (json[key]?.ToString() is { Length: > 0 } message)
                        return message;
                }
            }

            return string.IsNullOrWhiteSpace(Raw) ? $"HTTP {(int)Status}" : Raw;
        }
    }
}

internal static class JsonHttp
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    static JsonHttp()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-CE/1.0");
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static Task<JsonHttpResponse> PostFormAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        }, cancellationToken);

    public static Task<JsonHttpResponse> PostJsonAsync(
        string url,
        string json,
        CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, cancellationToken);

    public static Task<JsonHttpResponse> GetJsonAsync(
        string url,
        CancellationToken cancellationToken,
        string? bearerToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return SendAsync(request, cancellationToken);
    }

    private static async Task<JsonHttpResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JsonNode? body = null;

            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    body = JsonNode.Parse(raw);
                }
                catch (System.Text.Json.JsonException)
                {
                }
            }

            return new JsonHttpResponse(response.StatusCode, body, raw);
        }
    }
}
