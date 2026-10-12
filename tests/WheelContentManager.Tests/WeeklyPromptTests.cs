using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using Xunit;
namespace WheelContentManager.Tests;
public class WeeklyPromptTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    private sealed class Mailer : IWeeklyPromptMailer
    {
        public int Calls; public Exception? Failure;
        public Task SendAsync(WeeklyPromptBatch batch, CancellationToken ct)
        {
            Calls++; Assert.Equal(3, batch.Items.Count); Assert.All(batch.Items, i => Assert.True(File.Exists(i.Package!.Document)));
            if (Failure != null) throw Failure; return Task.CompletedTask;
        }
    }
    private static async Task<WeeklyPromptService> Service(TestEnvironment env, Mailer mailer, bool mail = true)
    {
        var settings = env.Services.GetRequiredService<SettingsService>(); var s = await settings.LoadAsync();
        s.WeeklyPromptEnabledAt = Monday; s.WeeklyPromptsEnabled = true; s.WeeklyPromptMailEnabled = mail;
        await settings.SaveAsync(s);
        return new(env.Factory, env.Content, settings, mailer, env.Services.GetRequiredService<AppPaths>());
    }
    [Theory][InlineData("2026-07-06T08:00:00Z", "2026-07-10T10:00:00Z")][InlineData("2026-12-07T08:00:00Z", "2026-12-11T11:00:00Z")]
    public void FridayUsesPolishNoonInBothSummerAndWinter(string now, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), WeeklyPromptService.FridayAtNoon(DateTimeOffset.Parse(now)));
    }
    [Fact] public async Task PreparesOnePackagePerBrandWithoutAiAndSendsOnlyAtDueTime()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        Assert.Equal(3, batch.Items.Count); Assert.Equal(3, batch.Items.Select(i => i.Brand).Distinct().Count());
        Assert.Equal(0, mailer.Calls); Assert.Empty(env.Ai.Calls);
        Assert.All(batch.Items, i => { Assert.Null(i.Package!.Photo); Assert.Contains("EN", i.Package.Prompt); });
        var reconstructed = await Service(env, mailer); await reconstructed.TickAsync(batch.Due);
        Assert.Equal(1, mailer.Calls); Assert.Equal("Sent", (await service.BatchesAsync()).Single().MailState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reconstructed.TickAsync(batch.Due.AddSeconds(30)));
        Assert.Equal(1, mailer.Calls); // No duplicate; the next week needs new galleries.
    }
    [Fact] public async Task MissedFridaySendsAfterRestartWithoutLosingPackages()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday); var due = (await service.BatchesAsync()).Single().Due;
        service = await Service(env, mailer); await service.TickAsync(due.AddDays(2));
        Assert.Equal(1, mailer.Calls); Assert.Equal("Sent", (await service.BatchesAsync()).Single().MailState);
    }
    [Fact] public async Task PartialPreparationOrNewlyPublishedTopicNeverSendsSuccessMail()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        env.Providers.Single(p => p.Brand == WheelBrand.Concaver).MissingProduct = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TickAsync(Monday)); Assert.Equal(0, mailer.Calls);
        env.Providers.Single(p => p.Brand == WheelBrand.Concaver).MissingProduct = false;
        await service.TickAsync(Monday); var due = (await service.BatchesAsync()).Single().Due;
        env.Blog.Result = new(true, false, "https://fixture.example/blog/new", "Duplikat");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TickAsync(due)); Assert.Equal(0, mailer.Calls); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task UnknownMailResultBlocksAutomaticRetryAndCanBeConfirmed()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer { Failure = new IOException("Lost SMTP response") }; var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = (await service.BatchesAsync()).Single();
        await Assert.ThrowsAsync<IOException>(() => service.TickAsync(batch.Due));
        await service.TickAsync(batch.Due.AddHours(1)); Assert.Equal(1, mailer.Calls); Assert.Equal("Unknown", (await service.BatchesAsync()).Single().MailState);
        await service.ResolveMailAsync(batch.Key, true); Assert.Equal("Sent", (await service.BatchesAsync()).Single().MailState);
    }
    [Fact] public async Task KnownUnsentMailRemainsPendingWithBoundedRetry()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer { Failure = new PromptMailNotSentException("Bad SMTP configuration") }; var service = await Service(env, mailer);
        await service.TickAsync(Monday); var due = (await service.BatchesAsync()).Single().Due;
        await service.TickAsync(due); Assert.Equal("Pending", (await service.BatchesAsync()).Single().MailState);
        await service.TickAsync(due.AddMinutes(1)); Assert.Equal(1, mailer.Calls);
        mailer.Failure = null; await service.TickAsync(due.AddMinutes(16)); Assert.Equal(2, mailer.Calls); Assert.Equal("Sent", (await service.BatchesAsync()).Single().MailState);
    }
    [Fact] public async Task PauseAndDisabledMailDoNotSend()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer, false);
        OperationSession.RequestCancellation(env.Services.GetRequiredService<AppPaths>());
        await service.TickAsync(Monday); Assert.Empty(await service.BatchesAsync());
        OperationSession.Resume(env.Services.GetRequiredService<AppPaths>());
        await service.TickAsync(Monday); var due = (await service.BatchesAsync()).Single().Due;
        await service.TickAsync(due); Assert.Equal(0, mailer.Calls);
    }
    [Fact] public async Task BackgroundMonitoringInvalidatesTopicPublishedAfterPreparation()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday);
        env.Blog.Result = new(true, false, "https://fixture.example/blog/new", "Nowy wpis");
        await service.TickAsync(Monday.AddHours(6));
        Assert.Equal(2, (await service.BatchesAsync()).Single().Items.Count); Assert.Equal(0, mailer.Calls);
    }
    [Fact] public void RankingSkipsUsedGalleriesAndLatestPublishedAndPreparedModels()
    {
        Gallery G(long id, string model, bool used = false) { var g = Fixtures.Gallery(); g.Id = id; g.Specification.Model = model; g.PromptUsed = used; return g; }
        var posts = new[] { new RecentBlogPost("https://jr-wheels.com/blog/a", "JR49", Monday, ["JR49"]) };
        var previous = new[] { new WeeklyPromptBatch { Due = Monday, Items = [new(99, WheelBrand.JR, "JR28", "", null)] } };
        var ranked = WeeklyPromptService.RankCandidates(WheelBrand.JR, new[] { G(1, "JR49"), G(2, "JR28"), G(3, "JR11", true), G(4, "SL03") }, posts, previous);
        Assert.Equal(4, Assert.Single(ranked).Id);
    }
    [Fact] public void WindowsTaskCallsOnlyPromptWorkflowAndCatchesMissedTriggers()
    {
        var xml = System.Xml.Linq.XDocument.Parse(WindowsScheduler.PromptTaskXml(@"C:\WT\Worker.exe", @"PC\User"));
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("--prepare-weekly-prompts", xml.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("true", xml.Descendants(ns + "StartWhenAvailable").Single().Value);
        Assert.Contains(xml.Descendants(ns + "Interval"), i => i.Value == "PT15M");
    }
}
