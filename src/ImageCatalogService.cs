using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SLC_DownloadManager;

public sealed class ImageCatalogService
{
    private readonly HttpClient _httpClient;

    public ImageCatalogService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<CatalogImage>> ListVhdxImagesAsync(string containerBaseUrl, string? prefix = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerBaseUrl))
        {
            throw new ArgumentException("Container base URL is required.", nameof(containerBaseUrl));
        }

        var results = new List<CatalogImage>();
        string marker = string.Empty;

        do
        {
            var requestUri = BuildListRequestUri(containerBaseUrl, prefix, marker);
            using var response = await _httpClient.GetAsync(requestUri, ct);
            response.EnsureSuccessStatusCode();

            string xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);

            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var blobNodes = doc.Descendants(ns + "Blob");

            foreach (var blobNode in blobNodes)
            {
                var name = blobNode.Element(ns + "Name")?.Value;
                if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var properties = blobNode.Element(ns + "Properties");
                var url = blobNode.Element(ns + "Url")?.Value;

                long? size = null;
                var contentLengthText = properties?.Element(ns + "Content-Length")?.Value;
                if (!string.IsNullOrWhiteSpace(contentLengthText) && long.TryParse(contentLengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                {
                    size = parsedSize;
                }

                DateTimeOffset? lastModified = null;
                var lastModifiedText = properties?.Element(ns + "Last-Modified")?.Value;
                if (!string.IsNullOrWhiteSpace(lastModifiedText) && DateTimeOffset.TryParse(lastModifiedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedLastModified))
                {
                    lastModified = parsedLastModified;
                }

                results.Add(new CatalogImage(name, url ?? string.Empty, size, lastModified));
            }

            marker = doc.Descendants(ns + "NextMarker").FirstOrDefault()?.Value ?? string.Empty;
        }
        while (!string.IsNullOrEmpty(marker));

        return results
            .OrderByDescending(i => i.LastModified ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildListRequestUri(string containerBaseUrl, string? prefix, string marker)
    {
        string normalizedBase = containerBaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? containerBaseUrl
            : containerBaseUrl + "/";
        string separator = containerBaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        var parts = new List<string>
        {
            "restype=container",
            "comp=list"
        };

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            parts.Add($"prefix={Uri.EscapeDataString(prefix)}");
        }

        if (!string.IsNullOrWhiteSpace(marker))
        {
            parts.Add($"marker={Uri.EscapeDataString(marker)}");
        }

        return $"{normalizedBase}{separator}{string.Join("&", parts)}";
    }
}

public sealed record CatalogImage(
    string Name,
    string Url,
    long? ContentLength,
    DateTimeOffset? LastModified
);