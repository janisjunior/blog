using System.Text.Json;
using HtmlAgilityPack;
using WheelContentManager.Core;
using WheelContentManager.GalleryProviders;
using Xunit;

namespace WheelContentManager.Tests;
public class RealParserTests
{
    private static SourceProfile Profile(string brand) => JsonSerializer.Deserialize<SourceProfile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Profiles", brand + ".json")))!;
    private static HtmlDocument Document(string name) { var d = new HtmlDocument(); d.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Live", name + ".html")); return d; }
    [Theory][InlineData("JR", "JR-detail", "https://jr-wheels.com/vehicle-gallery/2703", "Ford", "Focus ST", "JR46", "19x8.5", "19x8.5")][InlineData("Concaver", "Concaver-detail", "https://concaverwheels.com/gallery/1560", "Mercedes-Benz", "E63s", "Concaver CVR1", "20x9.5", "20x10")]
    public void ParsesRealDetailsAndAbsoluteFullImagePaths(string brand, string fixture, string url, string make, string model, string wheel, string front, string rear)
    {
        var b = Enum.Parse<WheelBrand>(brand); var result = ProfileGalleryProvider.Parse(Document(fixture), new(url), b, Profile(brand));
        Assert.Equal(make, result.Vehicle.Make); Assert.Equal(model, result.Vehicle.Model); Assert.Equal(wheel, result.Specification.Model); Assert.Equal(front, result.Specification.FrontSize); Assert.Equal(rear, result.Specification.RearSize);
        Assert.All(result.Images, x => Assert.DoesNotContain("/vehicle-gallery/zdjecia", x.Url)); Assert.NotEmpty(result.Images); Assert.Contains(result.Sources, x => x.Field == "CarModel" && x.Confirmed); Assert.NotNull(result.Specification.ProductUrl);
    }
    [Fact] public void DoesNotGuessConcaverWheelModelWhenSourceIsEmpty()
    {
        var result = ProfileGalleryProvider.Parse(Document("Concaver-missing"), new("https://concaverwheels.com/gallery/1597"), WheelBrand.Concaver, Profile("Concaver")); Assert.Null(result.Specification.Model); Assert.False(result.Complete); Assert.Contains("model felg", result.MissingData);
    }
    [Fact] public void VesserPreservesFrontRearCommaSizesAndVehicleOnlyImages()
    {
        var result = ProfileGalleryProvider.Parse(Document("Vesser-detail"), new("https://vesserforged.com/gallery/bmw-5-series-246"), WheelBrand.Vesser, Profile("Vesser")); Assert.Equal("VSR1", result.Specification.Model); Assert.Equal("Satin Bronze", result.Specification.Finish); Assert.Equal("21x9", result.Specification.FrontSize); Assert.Equal("21x10.5", result.Specification.RearSize); Assert.Equal(26, result.Images.Count); Assert.Equal("https://vesserforged.com/wheel/vsr1", result.Specification.ProductUrl);
    }
    [Fact] public void VesserNextPageSelectorMatchesRealPagination()
    {
        var doc = Document("Vesser-index"); var p = Profile("Vesser"); var next = doc.DocumentNode.SelectSingleNode(p.NextPageXPath!)!; Assert.Equal("galleries/vehicles/2/", next.GetAttributeValue("href", "")); Assert.Equal(10, doc.DocumentNode.SelectNodes(p.ListCardsXPath)!.Count);
    }
    [Theory][InlineData("JR", "JR-index")][InlineData("Concaver", "Concaver-index")]
    public void ActualIndexProfilesFindVehicleCardsRatherThanNavigation(string brand, string file) => Assert.Equal(12, Document(file).DocumentNode.SelectNodes(Profile(brand).ListCardsXPath)!.Count);
}
