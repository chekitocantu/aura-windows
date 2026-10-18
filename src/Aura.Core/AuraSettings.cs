using System.Text.Json;

namespace Aura.Core;

/// <summary>Everything Aura remembers between runs. Saved as JSON in %APPDATA%\Aura.</summary>
public sealed class AuraSettings
{
    /// <summary>"auto", "es" or "en".</summary>
    public string Language { get; set; } = "auto";

    public string LibraryFolder { get; set; } = "";

    /// <summary>Wallpaper per monitor, keyed by the monitor's device name (\\.\DISPLAY1).</summary>
    public Dictionary<string, string> Wallpapers { get; set; } = new();

    /// <summary>"fill" (crop to cover), "fit" (black bars) or "stretch".</summary>
    public string Fit { get; set; } = "fill";

    public bool Mute { get; set; } = true;
    public int Volume { get; set; } = 40;

    public bool HardwareDecoding { get; set; } = true;

    public bool PauseOnFullscreen { get; set; } = true;
    public bool PauseOnMaximized { get; set; } = false;
    public bool PauseOnBattery { get; set; } = true;
    public bool PauseOnLock { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    /// <summary>"4k" or "hd" for MotionBGS downloads.</summary>
    public string VideoQuality { get; set; } = "4k";

    /// <summary>The user pressed pause (as opposed to auto-pause).</summary>
    public bool UserPaused { get; set; }

    public bool TrayHintShown { get; set; }

    // ---- persistence ----

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AuraSettings Load(string file, string defaultLibrary)
    {
        AuraSettings s;
        try
        {
            s = File.Exists(file)
                ? JsonSerializer.Deserialize<AuraSettings>(File.ReadAllText(file)) ?? new AuraSettings()
                : new AuraSettings();
        }
        catch (Exception)
        {
            // A corrupt file must not stop the app from opening.
            s = new AuraSettings();
        }
        if (string.IsNullOrWhiteSpace(s.LibraryFolder)) s.LibraryFolder = defaultLibrary;
        s.Volume = Math.Clamp(s.Volume, 0, 100);
        if (s.Fit is not ("fill" or "fit" or "stretch")) s.Fit = "fill";
        if (s.VideoQuality is not ("4k" or "hd")) s.VideoQuality = "4k";
        return s;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, file, overwrite: true);
    }
}
