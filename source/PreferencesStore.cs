using System.Text.Json;
using System.IO;

namespace Yuhui;

public sealed class PreferencesStore
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "yuhui.settings.json");

    public bool AutoLocation { get; init; } = true;
    public string City { get; init; } = "杭州";
    public bool Topmost { get; init; } = true;
    public bool AutoHide { get; init; }
    public bool HourlyPeek { get; init; } = true;
    public bool MouseThrough { get; init; }
    public double Opacity { get; init; } = 92;
    public string Channel { get; init; } = "chillhop";
    public string? LocalAudioPath { get; init; }

    public static PreferencesStore Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<PreferencesStore>(json) ?? new PreferencesStore();
        }
        catch
        {
            return new PreferencesStore();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
