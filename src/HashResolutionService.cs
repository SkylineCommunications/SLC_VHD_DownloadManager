using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SLC_DownloadManager;

public sealed class HashResolutionService
{
    private static readonly Regex Sha256Regex = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);
    private readonly HttpClient _httpClient;

    public HashResolutionService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<HashResolutionResult?> ResolveAsync(
        string vhdxUrl,
        string? explicitHash,
        string? explicitHashUrl,
        bool autoResolve,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitHash))
        {
            string? parsed = TryExtractSha256(explicitHash);
            if (parsed == null)
            {
                throw new InvalidOperationException("Provided --hash value does not contain a valid SHA256 hash.");
            }

            return new HashResolutionResult(parsed, "explicit-hash");
        }

        if (!string.IsNullOrWhiteSpace(explicitHashUrl))
        {
            string content = await FetchTextAsync(explicitHashUrl, ct);
            string? parsed = TryExtractSha256(content);
            if (parsed == null)
            {
                throw new InvalidOperationException($"No SHA256 hash found at {explicitHashUrl}.");
            }

            return new HashResolutionResult(parsed, explicitHashUrl);
        }

        if (!autoResolve)
        {
            return null;
        }

        foreach (var candidate in BuildDerivedHashUrls(vhdxUrl))
        {
            try
            {
                string content = await FetchTextAsync(candidate, ct);
                string? parsed = TryExtractSha256(content);
                if (parsed != null)
                {
                    return new HashResolutionResult(parsed, candidate);
                }
            }
            catch (HttpRequestException)
            {
                // Try the next candidate.
            }
            catch (TaskCanceledException)
            {
                // Respect cancellation in caller path.
                throw;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> BuildDerivedHashUrls(string vhdxUrl)
    {
        if (string.IsNullOrWhiteSpace(vhdxUrl))
        {
            return Array.Empty<string>();
        }

        var candidates = new List<string>
        {
            vhdxUrl + ".sha256",
            vhdxUrl + ".sha256sum",
            vhdxUrl + ".hash"
        };

        if (vhdxUrl.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
        {
            string withoutExtension = vhdxUrl[..^5];
            candidates.Add(withoutExtension + ".sha256");
            candidates.Add(withoutExtension + ".sha256sum");
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? TryExtractSha256(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var match = Sha256Regex.Match(content);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private async Task<string> FetchTextAsync(string url, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}

public sealed record HashResolutionResult(string Hash, string Source);
