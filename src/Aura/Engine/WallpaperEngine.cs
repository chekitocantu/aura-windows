using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Aura.Core;
using Aura.Core.Library;

namespace Aura.Engine;

public sealed record ScreenInfo(string Device, Rectangle Bounds, bool IsPrimary, int Number);

/// <summary>
/// Keeps the desktop in line with <see cref="AuraSettings.Wallpapers"/>: one mpv per monitor that
/// has a video, the normal Windows wallpaper for monitors that have an image.
/// </summary>
internal sealed class WallpaperEngine : IDisposable
{
    private readonly AuraSettings _settings;
    private readonly Action _save;
    private DesktopLayer _layer = new();
    private readonly Dictionary<string, MpvPlayer> _players = new();
    private readonly HashSet<string> _autoPausedScreens = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _globalAutoPause;

    public event Action? Changed;
    public event Action<string>? Failed;

    public WallpaperEngine(AuraSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
    }

    internal DesktopLayer Layer => _layer;

    public static IReadOnlyList<ScreenInfo> Screens()
    {
        var all = Screen.AllScreens
            .OrderByDescending(s => s.Primary)
            .ThenBy(s => s.Bounds.X)
            .ThenBy(s => s.Bounds.Y)
            .ToList();
        return all.Select((s, i) => new ScreenInfo(s.DeviceName, s.Bounds, s.Primary, i + 1)).ToList();
    }

    public string? WallpaperOn(string device) =>
        _settings.Wallpapers.TryGetValue(device, out var f) ? f : null;

    public bool IsPlaying(string device) => _players.TryGetValue(device, out var p) && p.IsAlive;

    public bool AnyVideo => _players.Values.Any(p => p.IsAlive);

    public bool UserPaused => _settings.UserPaused;

    public bool AutoPaused => _globalAutoPause || _autoPausedScreens.Count > 0;

    public bool IsWallpaperFile(string file) =>
        _settings.Wallpapers.Values.Any(v => string.Equals(v, file, StringComparison.OrdinalIgnoreCase));

    public async Task ApplyAsync(string file, string? device)
    {
        if (!File.Exists(file))
        {
            Failed?.Invoke(Strings.F("err.missing", Path.GetFileName(file)));
            return;
        }
        var targets = Screens().Where(s => device is null || s.Device == device).ToList();
        foreach (var s in targets) _settings.Wallpapers[s.Device] = file;
        _settings.UserPaused = false;
        _save();
        await SyncAsync(forceRestart: targets.Select(t => t.Device).ToHashSet());
    }

    public async Task ClearAsync(string? device)
    {
        if (device is null) _settings.Wallpapers.Clear();
        else _settings.Wallpapers.Remove(device);
        _save();
        await SyncAsync();
    }

    /// <summary>Stops every player and starts again (after changing fit, decoding, monitors or Explorer restarting).</summary>
    public async Task RestartAllAsync(bool reinitDesktop = false)
    {
        await _gate.WaitAsync();
        try
        {
            StopAllUnlocked();
            if (reinitDesktop) _layer = new DesktopLayer();
        }
        finally { _gate.Release(); }
        await SyncAsync();
    }

    public async Task SyncAsync(HashSet<string>? forceRestart = null)
    {
        await _gate.WaitAsync();
        var stoppedAny = false;
        try
        {
            var screens = Screens();
            var byDevice = screens.ToDictionary(s => s.Device);

            // Forget files that were deleted.
            foreach (var (dev, file) in _settings.Wallpapers.ToList())
            {
                if (!File.Exists(file))
                {
                    Log.Write($"Wallpaper file missing, clearing {dev}: {file}");
                    _settings.Wallpapers.Remove(dev);
                }
            }

            // Stop players that are dead, moved, or no longer wanted.
            foreach (var (dev, player) in _players.ToList())
            {
                var want = WallpaperOn(dev);
                var keep = player.IsAlive
                           && byDevice.TryGetValue(dev, out var scr)
                           && scr.Bounds == player.Bounds
                           && want is not null
                           && string.Equals(want, player.File, StringComparison.OrdinalIgnoreCase)
                           && LibraryScanner.IsVideo(want)
                           && forceRestart?.Contains(dev) != true;
                if (!keep)
                {
                    player.Dispose();
                    _players.Remove(dev);
                    stoppedAny = true;
                }
            }

            if (!_layer.IsValid && !_layer.Initialize())
                Log.Write("Desktop layer not available yet");

            // Only one player plays sound: the primary screen if it has a video, else the first one.
            var audioDevice = screens.FirstOrDefault(s => WallpaperOn(s.Device) is { } f && LibraryScanner.IsVideo(f))?.Device;

            foreach (var screen in screens)
            {
                var file = WallpaperOn(screen.Device);
                if (file is null || _players.ContainsKey(screen.Device)) continue;

                if (LibraryScanner.IsVideo(file))
                {
                    try
                    {
                        var player = await MpvPlayer.StartAsync(file, screen.Device, screen.Bounds,
                            audio: screen.Device == audioDevice, _settings, _layer,
                            startPaused: ShouldPause(screen.Device), CancellationToken.None);
                        _players[screen.Device] = player;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"start player on {screen.Device}", ex);
                        Failed?.Invoke(Strings.F("err.apply", ex.Message));
                    }
                }
                else if (forceRestart?.Contains(screen.Device) == true)
                {
                    StaticWallpaper.Set(file, screen.Bounds, _settings.Fit);
                    _layer.EnsureWorkerWAtBottom();
                }
            }

            if (stoppedAny && _players.Count == 0) _layer.RefreshWallpaper();
            ApplyPauseUnlocked();
        }
        finally
        {
            _gate.Release();
        }
        _save();
        Changed?.Invoke();
    }

    public void SetUserPaused(bool paused)
    {
        _settings.UserPaused = paused;
        _save();
        ApplyPauseUnlocked();
        Changed?.Invoke();
    }

    public void SetAutoPause(IReadOnlyCollection<string> coveredScreens, bool global)
    {
        var changed = global != _globalAutoPause || !_autoPausedScreens.SetEquals(coveredScreens);
        if (!changed) return;
        _globalAutoPause = global;
        _autoPausedScreens.Clear();
        _autoPausedScreens.UnionWith(coveredScreens);
        ApplyPauseUnlocked();
        Changed?.Invoke();
    }

    public void UpdateAudio()
    {
        foreach (var p in _players.Values)
        {
            p.SetMute(_settings.Mute);
            p.SetVolume(_settings.Volume);
        }
    }

    private bool ShouldPause(string device) =>
        _settings.UserPaused || _globalAutoPause || _autoPausedScreens.Contains(device);

    private void ApplyPauseUnlocked()
    {
        foreach (var (dev, p) in _players.ToList())
            p.SetPaused(ShouldPause(dev));
    }

    /// <summary>For the self-test and the log: where each player window ended up.</summary>
    public IEnumerable<(string Device, IntPtr Window, IntPtr Parent)> Diagnostics() =>
        _players.Select(kv => (kv.Key, kv.Value.Window, Native.GetParent(kv.Value.Window)));

    private void StopAllUnlocked()
    {
        foreach (var p in _players.Values) p.Dispose();
        var had = _players.Count > 0;
        _players.Clear();
        if (had) _layer.RefreshWallpaper();
    }

    public void Dispose()
    {
        _gate.Wait();
        try { StopAllUnlocked(); }
        finally { _gate.Release(); }
    }
}
