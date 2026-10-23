using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers;

public class RemoteStreamRangeTests
{
    [Theory]
    [InlineData(HttpStatusCode.PartialContent)]
    [InlineData(HttpStatusCode.OK)]
    public async Task StaticRemoteStream_PreservesPartialContentStatusAndRange(HttpStatusCode upstreamStatus)
    {
        using var handler = new RangeHandler(upstreamStatus);
        using var upstream = new HttpClient(handler);
        using var host = await new HostBuilder()
            .ConfigureWebHost(builder => builder.UseTestServer()
                .ConfigureServices(services => services.AddControllers())
                .Configure(app => app.Run(async context =>
                {
                    using var state = new StreamState(Mock.Of<IMediaSourceManager>(), TranscodingJobType.Progressive, Mock.Of<ITranscodeManager>())
                    {
                        MediaPath = "https://storage.example.test/media", MediaSource = new MediaSourceInfo()
                    };
                    var result = await FileStreamResponseHelpers.GetStaticRemoteStreamResult(state, upstream, context, context.RequestAborted).ConfigureAwait(false);
                    await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor())).ConfigureAwait(false);
                })))
            .StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream");
        request.Headers.Range = new RangeHeaderValue(128, 255);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 128-255/512", response.Content.Headers.ContentRange!.ToString());
        Assert.Equal("bytes=128-255", handler.Range);
        Assert.Equal(128, (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken).ConfigureAwait(true)).Length);
    }

    private sealed class RangeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public RangeHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        public string? Range { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Range = request.Headers.Range?.ToString();
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(Enumerable.Repeat((byte)1, 128).ToArray())
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(128, 255, 512);
            response.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(response);
        }
    }
}
