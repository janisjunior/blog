using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WheelContentManager.Core;
using WheelContentManager.Infrastructure;
using Xunit;

namespace WheelContentManager.Tests;
public class EditorialDocumentTests
{
    private static string DocumentPath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wpisy na bloga.docx");
    [Fact] public void ActualDocumentIdentityAndAllSectionsArePreserved()
    {
        using var stream = File.OpenRead(DocumentPath);
        Assert.Equal(PromptService.EditorialDocumentSha256, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        var sections = PromptService.ReadDocument(DocumentPath); Assert.Equal(3, sections.Count);
        Assert.Contains("Model: JR50", sections[WheelBrand.JR]); Assert.Contains("Rear size: 20x8.5", sections[WheelBrand.Concaver]);
        Assert.Contains("Styl i konstrukcja zdań mają się różnić.", sections[WheelBrand.Vesser]);
    }
    [Theory][InlineData(WheelBrand.JR)][InlineData(WheelBrand.Concaver)][InlineData(WheelBrand.Vesser)]
    public void CompleteEditorialRequirementsRemainInEachTemplate(WheelBrand brand)
    {
        var source = PromptService.ReadDocument(DocumentPath)[brand]; var template = PromptService.Default(brand);
        foreach (var line in source.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            // Jawne zmiany dotyczą wyłącznie danych przykładowych, zmiennych oraz faktów wymagających weryfikacji.
            if (line.StartsWith("Model:") || line.StartsWith("Colour:") || line.StartsWith("Finish:") || line.StartsWith("Front size:") || line.StartsWith("Rear size:") || line.StartsWith("Car make:") || line.StartsWith("Car model:") || line == "+ PRODUCT CARD:" || line is "Wspomnij o homologacji TÜV" or "Podkreśl, że felgi posiadają homologację TÜV" or "Wspomnij, że model dostępny jest w rozmiarach od 19 do 23 cali") continue;
            Assert.Contains(line.Replace("[car model]", "{CAR_MODEL}").Replace("[model auta]", "{CAR_MODEL}"), template);
        }
        Assert.Contains("{VERIFIED_CERTIFICATIONS}", template); Assert.Contains("{AVAILABLE_SIZES}", template);
        Assert.Contains("dwa osobne zadania PL i EN", template); Assert.Contains("2200–2600", template);
    }
    [Theory][InlineData(WheelBrand.JR)][InlineData(WheelBrand.Concaver)][InlineData(WheelBrand.Vesser)]
    public void TemplatesRenderSelectedGalleryWithoutLeakingExampleConfiguration(WheelBrand brand)
    {
        var gallery = Fixtures.Gallery(brand); gallery.Vehicle.Make = "Fiat"; gallery.Vehicle.Model = "Panda"; gallery.Specification.Model = brand == WheelBrand.Concaver ? "Concaver CVR7" : brand == WheelBrand.JR ? "JR46" : "Vesser VSR9"; gallery.Specification.Finish = "Selected finish";
        var rendered = PromptRenderer.Render(PromptService.Default(brand), gallery);
        Assert.Contains("Fiat Panda", rendered); Assert.Contains(gallery.Specification.Model, rendered); Assert.Contains("Selected finish", rendered);
        Assert.DoesNotContain("Matt Bronze", rendered); Assert.DoesNotContain("Gloss Blue-Purple Chameleon", rendered); Assert.DoesNotContain("3 Series / M3", rendered); Assert.DoesNotContain("A3 / S3 / RS3", rendered); Assert.DoesNotContain("Model: JR50", rendered);
    }
    [Fact] public async Task FreshInstallHasVerifiedDocumentTemplatesAndKnownImportIsIdempotent()
    {
        await using var env = await TestEnvironment.CreateAsync(); var prompts = env.Services.GetRequiredService<PromptService>();
        foreach (var brand in Enum.GetValues<WheelBrand>()) { var current = await prompts.CurrentAsync(brand); Assert.True(current.EditorialDocumentVerified); Assert.Equal(PromptService.Default(brand), current.Content); }
        await prompts.ImportAsync(DocumentPath); await prompts.ImportAsync(DocumentPath);
        foreach (var brand in Enum.GetValues<WheelBrand>()) Assert.Single(await prompts.HistoryAsync(brand));
        await prompts.SaveAsync(WheelBrand.JR, "Ręczna wersja", "użytkownik", false); await prompts.ImportAsync(DocumentPath);
        Assert.True((await prompts.CurrentAsync(WheelBrand.JR)).EditorialDocumentVerified); Assert.Equal(3, (await prompts.HistoryAsync(WheelBrand.JR)).Count);
    }
    [Fact] public async Task UpgradeReplacesOnlyUntouchedLegacyDefaultsAndKeepsHistoryAndUserEdits()
    {
        await using var env = await TestEnvironment.CreateAsync(); await using (var db = await env.Factory.CreateDbContextAsync())
        {
            foreach (var brand in new[] { WheelBrand.JR, WheelBrand.Concaver })
            {
                var template = await db.PromptTemplates.Include(x => x.Versions).SingleAsync(x => x.Brand == brand);
                var first = template.Versions.Single(); first.Content = "# Szablon roboczy — wymaga dokumentu redakcyjnego\nLegacy"; first.Origin = PromptService.LegacyDefaultOrigin; first.EditorialDocumentVerified = false;
            }
            await db.SaveChangesAsync();
        }
        var prompts = env.Services.GetRequiredService<PromptService>(); await prompts.SaveAsync(WheelBrand.Concaver, "Własny prompt {CAR_MODEL}", "edycja użytkownika", false);
        await Bootstrap.InitializeAsync(env.Services); await Bootstrap.InitializeAsync(env.Services);
        Assert.Equal(PromptService.Default(WheelBrand.JR), (await prompts.CurrentAsync(WheelBrand.JR)).Content); Assert.True((await prompts.CurrentAsync(WheelBrand.JR)).EditorialDocumentVerified); Assert.Equal(2, (await prompts.HistoryAsync(WheelBrand.JR)).Count);
        Assert.Equal("Własny prompt {CAR_MODEL}", (await prompts.CurrentAsync(WheelBrand.Concaver)).Content); Assert.False((await prompts.CurrentAsync(WheelBrand.Concaver)).EditorialDocumentVerified); Assert.Equal(2, (await prompts.HistoryAsync(WheelBrand.Concaver)).Count);
    }
    [Fact] public async Task DifferentDocumentWithSameFilenameStillRequiresReview()
    {
        await using var env = await TestEnvironment.CreateAsync(); var path = Path.Combine(env.Root, "Wpisy na bloga.docx");
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var part = doc.AddMainDocumentPart(); part.Document = new Document(new Body());
            foreach (var heading in new[] { "JR", "CVR", "VSR" }) { part.Document.Body!.Append(new Paragraph(new Run(new Text(heading))), new Paragraph(new Run(new Text("Zmienione wymagania dla " + heading)))); }
            part.Document.Save();
        }
        var prompts = env.Services.GetRequiredService<PromptService>(); await prompts.ImportAsync(path);
        foreach (var brand in Enum.GetValues<WheelBrand>()) { var current = await prompts.CurrentAsync(brand); Assert.False(current.EditorialDocumentVerified); Assert.Contains("Zmienione wymagania", current.Content); }
    }
}
