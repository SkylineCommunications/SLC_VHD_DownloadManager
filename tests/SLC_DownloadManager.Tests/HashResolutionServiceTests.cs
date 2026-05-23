using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SLC_DownloadManager.Tests;

public class HashResolutionServiceTests
{
    [Fact]
    public void TryExtractSha256_ParsesRawHash()
    {
        string hash = "a18b22343405cb97be56bef0832d09687f8408d5466dc8e251d435a0f65a70e3";
        string? parsed = HashResolutionService.TryExtractSha256(hash);

        Assert.Equal(hash, parsed);
    }

    [Fact]
    public void TryExtractSha256_ParsesChecksumLine()
    {
        string hash = "a18b22343405cb97be56bef0832d09687f8408d5466dc8e251d435a0f65a70e3";
        string line = hash + "  mgdsk.vhdx";

        string? parsed = HashResolutionService.TryExtractSha256(line);

        Assert.Equal(hash, parsed);
    }

    [Fact]
    public void BuildDerivedHashUrls_IncludesExpectedCandidates()
    {
        const string url = "https://example.test/file.vhdx";

        var urls = HashResolutionService.BuildDerivedHashUrls(url);

        Assert.Contains("https://example.test/file.vhdx.sha256", urls);
        Assert.Contains("https://example.test/file.vhdx.sha256sum", urls);
        Assert.Contains("https://example.test/file.vhdx.hash", urls);
        Assert.Contains("https://example.test/file.sha256", urls);
        Assert.Contains("https://example.test/file.sha256sum", urls);
    }

    [Fact]
    public async Task ResolveAsync_UsesExplicitHashFirst()
    {
        var service = new HashResolutionService(new HttpClient(new StubMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var result = await service.ResolveAsync(
            "https://example.test/file.vhdx",
            explicitHash: "a18b22343405cb97be56bef0832d09687f8408d5466dc8e251d435a0f65a70e3",
            explicitHashUrl: null,
            autoResolve: true,
            ct: CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("explicit-hash", result!.Source);
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
