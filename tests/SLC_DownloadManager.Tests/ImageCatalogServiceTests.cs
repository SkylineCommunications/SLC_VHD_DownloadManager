using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SLC_DownloadManager.Tests;

public class ImageCatalogServiceTests
{
    [Fact]
    public async Task ListVhdxImagesAsync_FiltersAndPagesResults()
    {
        var responses = new Queue<string>();
        responses.Enqueue("""
<EnumerationResults>
  <Blobs>
    <Blob>
      <Name>10.6/image-a.vhdx</Name>
      <Url>https://example.test/10.6/image-a.vhdx</Url>
      <Properties>
        <Content-Length>100</Content-Length>
        <Last-Modified>Tue, 28 Apr 2026 11:08:20 GMT</Last-Modified>
      </Properties>
    </Blob>
    <Blob>
      <Name>10.6/image-a.qcow2</Name>
      <Url>https://example.test/10.6/image-a.qcow2</Url>
      <Properties>
        <Content-Length>200</Content-Length>
      </Properties>
    </Blob>
  </Blobs>
  <NextMarker>page-2</NextMarker>
</EnumerationResults>
""");
        responses.Enqueue("""
<EnumerationResults>
  <Blobs>
    <Blob>
      <Name>10.5/image-b.vhdx</Name>
      <Url>https://example.test/10.5/image-b.vhdx</Url>
      <Properties>
        <Content-Length>300</Content-Length>
        <Last-Modified>Tue, 21 Apr 2026 07:13:19 GMT</Last-Modified>
      </Properties>
    </Blob>
  </Blobs>
  <NextMarker></NextMarker>
</EnumerationResults>
""");

        var service = new ImageCatalogService(new HttpClient(new SequenceMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue())
            })));

        var images = await service.ListVhdxImagesAsync("https://example.test/container/", "10.", CancellationToken.None);

        Assert.Equal(2, images.Count);
        Assert.All(images, i => Assert.EndsWith(".vhdx", i.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("10.6/image-a.vhdx", images[0].Name);
        Assert.Equal("10.5/image-b.vhdx", images[1].Name);
    }

    private sealed class SequenceMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public SequenceMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_factory(request));
        }
    }
}
