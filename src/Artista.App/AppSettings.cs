using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Artista.App;

/// <summary>
/// Persisted application settings (JSON in %AppData%\Artista\settings.json).
/// </summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public int DefaultDocumentWidth { get; set; } = 800;
    public int DefaultDocumentHeight { get; set; } = 600;
    public string DefaultDocumentBackground { get; set; } = "White"; // Transparent | White | Color
    public uint DefaultDocumentColor { get; set; } = 0xFFFFFFFF;
    public long HistoryMemoryLimitMb { get; set; } = 512;
    public List<string> RecentFiles { get; set; } = new();
    public List<uint> Palette { get; set; } = new();
    public bool ShowPixelGrid { get; set; } = true;
    public bool ShowRulers { get; set; }
    public int JpegQuality { get; set; } = 92;

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Artista", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings persistence is best-effort; never crash the app for it.
        }
    }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10)
            RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }
}
