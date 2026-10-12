using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using WheelContentManager.GalleryProviders;
using Xunit;
namespace WheelContentManager.Tests;
public class SiteClientConcurrencyTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OtherHost { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Paths { get; } = new();
        public long FirstFinished, SecondStarted;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/robots.txt") return new(HttpStatusCode.NotFound);
            Paths.Enqueue(uri.AbsolutePath);
            if (uri.AbsolutePath == "/first") { First.TrySetResult(); await Release.Task.WaitAsync(ct); FirstFinished = Stopwatch.GetTimestamp(); }
            if (uri.AbsolutePath == "/other") OtherHost.TrySetResult();
            if (uri.AbsolutePath == "/second") SecondStarted = Stopwatch.GetTimestamp();
            return new(HttpStatusCode.OK) { Content = new StringContent("fixture") };
        }
    }
    [Fact] public async Task DifferentHostsOverlapWhileSameHostKeepsItsRateLimit()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler); var client = new SiteClient(http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = client.GetAsync(new("https://jr-wheels.com/first"), false, timeout.Token);
        await handler.First.Task.WaitAsync(timeout.Token);
        var second = client.GetAsync(new("https://jr-wheels.com/second"), false, timeout.Token);
        var other = client.GetAsync(new("https://concaverwheels.com/other"), false, timeout.Token);
        try { await handler.OtherHost.Task.WaitAsync(timeout.Token); Assert.DoesNotContain("/second", handler.Paths); }
        finally { handler.Release.TrySetResult(); await Task.WhenAll(first, second, other); }
        Assert.True(Stopwatch.GetElapsedTime(handler.FirstFinished, handler.SecondStarted) >= TimeSpan.FromMilliseconds(900));
    }
}
