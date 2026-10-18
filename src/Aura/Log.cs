using System;
using System.IO;

namespace Aura;

/// <summary>Plain text log in %LOCALAPPDATA%\Aura\logs, one file per day, last 7 kept.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    public static string Folder => Path.Combine(AppPaths.LocalData, "logs");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"aura-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public static void Error(string where, Exception ex) => Write($"ERROR {where}: {ex}");

    public static void Prune()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Folder, "aura-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch { }
    }
}

internal static class AppPaths
{
    public static string Roaming => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura");
    public static string LocalData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura");
    public static string SettingsFile => Path.Combine(Roaming, "settings.json");
    public static string Cache => Path.Combine(LocalData, "cache");
    public static string ThumbCache => Path.Combine(Cache, "thumbs");
    public static string DefaultLibrary =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Aura");

    /// <summary>mpv ships next to Aura.exe in an "mpv" folder.</summary>
    public static string MpvExe => Path.Combine(AppContext.BaseDirectory, "mpv", "mpv.exe");
}
