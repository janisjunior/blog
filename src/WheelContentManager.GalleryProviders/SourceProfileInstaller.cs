using System.Security.Cryptography;
namespace WheelContentManager.GalleryProviders;

public static class SourceProfileInstaller
{
    private static readonly Dictionary<string, string> Previous = new()
    {
        ["JR.json"] = "34DA2852883184BA132E77DF86DE1898CE66C2C5273E134E43E73464A3CF7EB7",
        ["Concaver.json"] = "BC4FB34ED39E5265689973EA6BE2D3AE014A6AA583E59E59DAA3DEDA1F54B488",
    };
    public static void Install(string shipped, string destination)
    {
        Directory.CreateDirectory(destination); if (!Directory.Exists(shipped)) return;
        foreach (var source in Directory.EnumerateFiles(shipped, "*.json"))
        {
            var name = Path.GetFileName(source); var target = Path.Combine(destination, name);
            if (File.Exists(target) && (!Previous.TryGetValue(name, out var hash) || Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(target).Replace("\r\n", "\n")))) != hash)) continue;
            // Replace only a known untouched built-in profile; retain custom selectors.
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(source, temporary); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
