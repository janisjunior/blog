using Microsoft.EntityFrameworkCore;
using WheelContentManager.Infrastructure;
using Xunit;

namespace WheelContentManager.Tests;
public class OperationControlTests
{
    [Fact] public async Task CancellationTargetsOwnerPausesAndReleasesLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "wt-control-" + Guid.NewGuid()); var paths = new AppPaths(root);
        try
        {
            using (var operation = OperationSession.Start(paths, "Pisanie", default))
            {
                operation.Progress(null).Report("Generowanie EN");
                Assert.Equal("Generowanie EN", OperationSession.Read(paths)!.Progress);
                Assert.Throws<OperationBusyException>(() => OperationSession.Start(paths, "Drugi", default));
                Assert.True(OperationSession.RequestCancellation(paths));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(TimeSpan.FromSeconds(5), operation.Token));
                Assert.True(OperationSession.Paused(paths));
            }
            Assert.False(OperationSession.IsRunning(paths)); Assert.Null(OperationSession.Read(paths));
            using var next = OperationSession.Start(paths, "Następny", default);
            await Task.Delay(300); Assert.False(next.Token.IsCancellationRequested);
            OperationSession.Resume(paths); Assert.False(OperationSession.Paused(paths));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task PausedStockHasNoAiDiscoveryOrNewRun()
    {
        await using var env = await TestEnvironment.CreateAsync();
        OperationSession.RequestCancellation(new AppPaths(env.Root));
        Assert.Contains("wstrzymano", await env.Content.PrepareReadySetAsync(null, default));
        Assert.Empty(env.Ai.Calls); Assert.Empty(await env.Content.RunsAsync());
        Assert.All(env.Providers, p => Assert.Equal(0, p.Discoveries));
    }
    [Fact] public async Task LanguagesOverlapAndUsageIsNotLost()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default);
        env.Ai.HoldWriting = true;
        var writing = env.Content.GenerateAsync((await env.Content.GalleriesAsync())[0].Id, false, false, null, null, default);
        try { await env.Ai.BothLanguagesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { env.Ai.ReleaseWriting.TrySetResult(); }
        var article = await writing;
        Assert.Equal(WheelContentManager.Core.ArticleStatus.Ready, article.Status); Assert.Equal(2, article.Versions.Count);
        await using var db = await env.Factory.CreateDbContextAsync(); var job = await db.GenerationJobs.SingleAsync();
        Assert.Equal(250, job.InputTokens); Assert.Equal(250, job.OutputTokens);
    }
    [Fact] public async Task CancellationKeepsCompletedLanguageAndResumeSkipsIt()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default);
        env.Ai.HoldWriting = true; env.Ai.HoldEnglishOnly = true;
        var gallery = (await env.Content.GalleriesAsync())[0];
        var writing = env.Content.GenerateAsync(gallery.Id, false, false, null, null, default);
        await env.Ai.BothLanguagesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!(await env.Content.ArticlesAsync()).Single().Versions.Any() && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
        Assert.Equal("PL", Assert.Single((await env.Content.ArticlesAsync()).Single().Versions).Language);
        OperationSession.RequestCancellation(new AppPaths(env.Root));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        Assert.Equal("PL", Assert.Single((await env.Content.ArticlesAsync()).Single().Versions).Language);
        env.Ai.HoldWriting = false;
        var resumed = await env.Content.GenerateAsync(gallery.Id, false, false, null, null, default);
        Assert.Equal(2, resumed.Versions.Count); Assert.Equal(WheelContentManager.Core.ArticleStatus.Ready, resumed.Status);
        Assert.Single(resumed.Versions, v => v.Language == "PL");
    }
    [Fact] public async Task CancelDuringLanguageWritingSavesFailureAndReservesCosts()
    {
        await using var env = await TestEnvironment.CreateAsync(); await env.Content.SyncAsync(null, default); env.Ai.HoldWriting = true;
        var writing = env.Content.GenerateAsync((await env.Content.GalleriesAsync())[0].Id, false, false, null, null, default);
        await env.Ai.BothLanguagesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(OperationSession.RequestCancellation(new AppPaths(env.Root)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        Assert.False(OperationSession.IsRunning(new AppPaths(env.Root)));
        var article = Assert.Single(await env.Content.ArticlesAsync()); Assert.Equal(WheelContentManager.Core.ArticleStatus.NeedsCorrection, article.Status);
        await using var db = await env.Factory.CreateDbContextAsync(); Assert.True((await db.GenerationJobs.SingleAsync()).OutputTokens > 50);
    }
}
