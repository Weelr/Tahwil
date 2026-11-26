using System.Text.Json;

namespace Tahwil.Core;

public sealed class Settings
{
    public DateTime LastUpdateCheck { get; set; }
    public string? PendingInstaller { get; set; }
    public string? PendingVersion { get; set; }
    /// <summary>Remembered Custom-dialog choices, per category key.</summary>
    public Dictionary<string, ConversionOptions> Remembered { get; set; } = [];

    static readonly string FilePath = Path.Combine(AppPaths.DataDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); } catch { }
    }
}
