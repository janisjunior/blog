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
    [Fact] public async Task FailedBrandDoesNotBlockOtherTwoPackagesOrSendPartialMail()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        env.Blog.RecentFailure = WheelBrand.JR;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TickAsync(Monday));
        Assert.Contains("2/3", error.Message);
        var batch = Assert.Single(await service.BatchesAsync());
        Assert.Equal(new[] { WheelBrand.Concaver, WheelBrand.Vesser }, batch.Items.Where(i => i.Package != null).Select(i => i.Brand));
        Assert.Equal(0, mailer.Calls);
        env.Blog.RecentFailure = null; await service.TickAsync(Monday.AddMinutes(15));
        Assert.Equal(3, (await service.BatchesAsync()).Single().Items.Count(i => i.Package != null)); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public void RankingSkipsUsedGalleriesAndLatestPublishedAndPreparedModels()
    {
        Gallery G(long id, string model, bool used = false) { var g = Fixtures.Gallery(); g.Id = id; g.Specification.Model = model; g.PromptUsed = used; return g; }
        var posts = new[] { new RecentBlogPost("https://jr-wheels.com/blog/a", "JR49", Monday, ["JR49"]) };
        var previous = new[] { new WeeklyPromptBatch { Due = Monday, Items = [new(99, WheelBrand.JR, "JR28", "", null)] } };
        var ranked = WeeklyPromptService.RankCandidates(WheelBrand.JR, new[] { G(1, "JR49"), G(2, "JR28"), G(3, "JR11", true), G(4, "SL03") }, posts, previous);
        Assert.Equal(4, Assert.Single(ranked).Id);
    }
    [Fact] public async Task ManualMailSendsBeforeFridayWithAutomaticMailDisabledAndNeverSendsTwice()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer, false);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        OperationSession.RequestCancellation(env.Services.GetRequiredService<AppPaths>());
        await service.SendNowAsync(batch.Key, Monday.AddMinutes(1));
        Assert.Equal(1, mailer.Calls); Assert.Equal("Sent", (await service.BatchesAsync()).Single().MailState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendNowAsync(batch.Key, Monday.AddMinutes(2)));
        OperationSession.Resume(env.Services.GetRequiredService<AppPaths>());
        await Service(env, mailer, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TickAsync(batch.Due));
        Assert.Equal(1, mailer.Calls); Assert.Empty(env.Ai.Calls);
    }
    [Fact] public async Task ManualMailRechecksPublicationAndRejectsIncompleteSet()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        env.Blog.Result = new(true, false, "https://fixture.example/blog/new", "Nowy wpis");
        var status = await service.SendNowAsync(batch.Key, Monday.AddMinutes(1));
        Assert.Contains("Mail nie został wysłany", status); Assert.Equal(0, mailer.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendNowAsync(batch.Key, Monday.AddMinutes(2)));
        Assert.Equal(0, mailer.Calls);
    }
    [Fact] public async Task ManualMailCannotRetryAnUncertainDelivery()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer { Failure = new IOException("Lost response") }; var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        await Assert.ThrowsAsync<IOException>(() => service.SendNowAsync(batch.Key, Monday.AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendNowAsync(batch.Key, Monday.AddMinutes(2)));
        Assert.Equal(1, mailer.Calls); Assert.Equal("Unknown", (await service.BatchesAsync()).Single().MailState);
    }
    [Fact] public async Task WeeklyMailContainsEachGalleryAndChatGptLinksInHtmlAndPlainText()
    {
        await using var env = await TestEnvironment.CreateAsync(); var service = await Service(env, new Mailer());
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        var (text, html) = NotificationService.WeeklyMessageBody(batch);
        Assert.Contains("https://chatgpt.com/", text); Assert.Contains("href=\"https://chatgpt.com/\"", html);
        foreach (var item in batch.Items) { Assert.Contains(item.Package!.GalleryUrl, text); Assert.Contains("href=\"" + item.Package.GalleryUrl + "\"", html); }
        Assert.Contains("wtblog://weekly/" + batch.Key, html);
    }
    private sealed class CaptureProgress : IProgress<string> { public List<string> Messages { get; } = []; public void Report(string value) => Messages.Add(value); }
    [Fact] public async Task ManualMailReportsEveryBrandBeforeSmtp()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        var progress = new CaptureProgress(); await service.SendNowAsync(batch.Key, Monday.AddMinutes(1), progress: progress);
        foreach (var brand in Enum.GetValues<WheelBrand>()) Assert.Contains(progress.Messages, m => m.StartsWith(brand.Name()));
        Assert.Contains("SMTP", progress.Messages.Last()); Assert.Equal(1, mailer.Calls);
    }
    [Fact] public async Task SlowBlogCheckTimesOutBeforeMailAndPreservesPendingBatch()
    {
        await using var env = await TestEnvironment.CreateAsync(); var mailer = new Mailer(); var service = await Service(env, mailer);
        await service.TickAsync(Monday); var batch = Assert.Single(await service.BatchesAsync());
        var timed = new WeeklyPromptService(env.Factory, env.Content, env.Services.GetRequiredService<SettingsService>(), mailer, env.Services.GetRequiredService<AppPaths>(), TimeSpan.FromMilliseconds(30));
        env.Blog.RecentDelay = TimeSpan.FromSeconds(1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => timed.SendNowAsync(batch.Key, Monday.AddMinutes(1)));
        Assert.Contains("limit czasu", error.Message); Assert.Contains("Mail nie został wysłany", error.Message);
        Assert.Equal(0, mailer.Calls); var saved = Assert.Single(await service.BatchesAsync()); Assert.Equal("Pending", saved.MailState); Assert.Null(saved.LastMailAttempt);
        Assert.False(OperationSession.IsRunning(env.Services.GetRequiredService<AppPaths>()));
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
