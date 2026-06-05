using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiModelCatalogService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<IReadOnlyList<string>> ListOllamaModelsAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(baseUrl.TrimEnd('/') + "/api/tags", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<string>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseOllamaTags(json);
    }

    public async Task<IReadOnlyList<string>> ListRemoteModelsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Array.Empty<string>();

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/v1/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Array.Empty<string>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseRemoteModels(json);
    }

    public static IReadOnlyList<string> ParseOllamaTags(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return models.EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : "")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> ParseRemoteModels(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return data.EnumerateArray()
                .Select(model => model.TryGetProperty("id", out var id) ? id.GetString() : "")
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
