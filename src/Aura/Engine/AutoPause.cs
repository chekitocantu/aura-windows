using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Threading;
using Aura.Core;
using Microsoft.Win32;
using static Aura.Engine.Native;

namespace Aura.Engine;

/// <summary>
/// Checks once a second whether each monitor is hidden by a full-screen app (games, videos) or,
/// if enabled, by a maximized window, and pauses only that monitor. Also pauses everything on
/// battery or while the session is locked.
/// </summary>
internal sealed class AutoPause : IDisposable
{
    private readonly WallpaperEngine _engine;
    private readonly AuraSettings _settings;
    private readonly DispatcherTimer _timer;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private bool _locked;

    public AutoPause(WallpaperEngine engine, AuraSettings settings)
    {
        _engine = engine;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Check();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void Start() => _timer.Start();

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        _locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect;
        Check();
    }

    private void Check()
    {
        if (!_engine.AnyVideo)
        {
            _engine.SetAutoPause(Array.Empty<string>(), false);
            return;
        }

        var global = (_settings.PauseOnLock && _locked) || (_settings.PauseOnBattery && OnBattery());
        var covered = new HashSet<string>();
        try
        {
            if (_settings.PauseOnFullscreen) AddFullscreen(covered);
            if (_settings.PauseOnMaximized) AddMaximized(covered);
        }
        catch (Exception ex)
        {
            Log.Error("AutoPause.Check", ex);
        }
        _engine.SetAutoPause(covered, global);
    }

    private static bool OnBattery()
    {
        var ps = SystemInformation.PowerStatus;
        return ps.PowerLineStatus == PowerLineStatus.Offline &&
               (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0;
    }

    // The foreground window covers its whole monitor: a game, a full-screen video, a presentation.
    private void AddFullscreen(HashSet<string> covered)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || _engine.Layer.IsDesktopWindow(fg) || !IsWindowVisible(fg) || IsIconic(fg)) return;
        if (!GetWindowRect(fg, out var r)) return;

        var mon = MonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST));
        var m = mon.rcMonitor;
        if (r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom)
            covered.Add(mon.szDevice);
    }

    // Any normal maximized window on a monitor hides most of its wallpaper.
    private void AddMaximized(HashSet<string> covered)
    {
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || !IsZoomed(h) || IsCloaked(h)) return true;
            var ex = GetStyle(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_TRANSPARENT) != 0) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == _ownPid || _engine.Layer.IsDesktopWindow(h)) return true;
            covered.Add(MonitorInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST)).szDevice);
            return true;
        }, IntPtr.Zero);
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
