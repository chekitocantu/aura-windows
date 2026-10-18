using System;
using Aura.Core;
using Aura.Engine;

namespace Aura;

/// <summary>What every page needs: settings, the engine, and a few app-wide events.</summary>
internal sealed class AppState
{
    public static AppState Current { get; set; } = null!;

    private readonly string _settingsFile;

    public AppState(AuraSettings settings, string settingsFile)
    {
        Settings = settings;
        _settingsFile = settingsFile;
        Engine = new WallpaperEngine(settings, Save);
    }

    public AuraSettings Settings { get; }
    public WallpaperEngine Engine { get; }

    /// <summary>Monitor the Library applies to; null means all monitors.</summary>
    public string? TargetDevice { get; set; }

    public event Action? LibraryChanged;
    public event Action<string>? ToastRequested;
    public event Action<string>? NavigateRequested;

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();
    public void Toast(string message) => ToastRequested?.Invoke(message);
    public void Navigate(string page) => NavigateRequested?.Invoke(page);

    public void Save()
    {
        try { Settings.Save(_settingsFile); }
        catch (Exception ex) { Log.Error("Settings.Save", ex); }
    }
}
