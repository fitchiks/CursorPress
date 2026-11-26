using System.Text.Json;

namespace CursorPress;

internal sealed class Settings
{
    public int Compression { get; set; } = 18;
    public int PressMs { get; set; } = 100;
    public int ReleaseMs { get; set; } = 170;
    public bool RightButton { get; set; } = true;
    public bool MiddleButton { get; set; }
    public bool AnimationEnabled { get; set; }
    public string Language { get; set; } = UiText.DetectLanguage();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorPress", "settings.json");
    public static Settings Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            value.Compression = Math.Clamp(value.Compression, 5, 40);
            value.PressMs = Math.Clamp(value.PressMs, 50, 250);
            value.ReleaseMs = Math.Clamp(value.ReleaseMs, 70, 350);
            if (value.Language is not ("ru" or "en")) value.Language = UiText.DetectLanguage();
            return value;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
