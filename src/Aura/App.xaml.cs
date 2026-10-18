using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Aura.Core;
using Aura.Engine;
using Microsoft.Win32;

namespace Aura;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showSignal;
    private Tray? _tray;
    private AutoPause? _autoPause;
    private MainWindow? _window;
    private DispatcherTimer? _displayDebounce;

    internal static bool StartedInBackground { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("UI", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write($"FATAL {ex.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("task", ex.Exception); ex.SetObserved(); };

        // Child processes (mpv) inherit this: a missing DLL makes them exit with an error code
        // instead of showing a "System Error" box that nobody sees behind the desktop.
        Native.SetErrorMode(Native.SEM_FAILCRITICALERRORS);

        var args = e.Args;
        var selfTest = Array.IndexOf(args, "--selftest");
        StartedInBackground = args.Contains("--background");

        if (selfTest < 0 && !TakeSingleInstance(args.Contains("--restart")))
        {
            Shutdown();
            return;
        }

        Log.Prune();
        Log.Write($"Aura {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion} args=[{string.Join(' ', args)}]");

        var settingsFile = selfTest >= 0 && args.Length > selfTest + 1
            ? Path.Combine(args[selfTest + 1], "settings.json")
            : AppPaths.SettingsFile;
        var settings = AuraSettings.Load(settingsFile, AppPaths.DefaultLibrary);
        Strings.Init(settings.Language);
        Directory.CreateDirectory(settings.LibraryFolder);

        var state = new AppState(settings, settingsFile);
        AppState.Current = state;
        state.Engine.Failed += msg => Dispatcher.BeginInvoke(() => state.Toast(msg));

        if (selfTest >= 0)
        {
            var outDir = args.Length > selfTest + 1 ? args[selfTest + 1] : Path.Combine(Path.GetTempPath(), "aura-selftest");
            var code = await SelfTest.RunAsync(state, outDir);
            state.Engine.Dispose();
            Shutdown(code);
            return;
        }

        if (settings.StartWithWindows) AutoStart.Apply(true);

        _window = new MainWindow();
        HookShellMessages(_window);

        _tray = new Tray(ShowMain, ExitApp);
        state.Engine.Changed += () => Dispatcher.BeginInvoke(() => _tray?.Refresh());

        _autoPause = new AutoPause(state.Engine, settings);
        _autoPause.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        if (!StartedInBackground) ShowMain();

        // At sign-in Explorer may not have drawn the desktop yet.
        await WaitForDesktopAsync();
        await state.Engine.SyncAsync(WallpaperEngine.Screens().Select(s => s.Device).ToHashSet());
        _tray.Refresh();
    }

    private bool TakeSingleInstance(bool restarting)
    {
        var user = Environment.UserName;
        _single = new Mutex(false, $"Aura-Windows-{user}");
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"Aura-Windows-Show-{user}");
        bool first;
        try { first = _single.WaitOne(restarting ? 8000 : 0); }
        catch (AbandonedMutexException) { first = true; }
        if (!first)
        {
            _showSignal.Set();
            return false;
        }

        var thread = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(ShowMain);
        }) { IsBackground = true, Name = "show-signal" };
        thread.Start();
        return true;
    }

    private static async Task WaitForDesktopAsync()
    {
        for (var i = 0; i < 60 && Native.FindWindow("Progman", null) == IntPtr.Zero; i++)
            await Task.Delay(1000);
        if (App.StartedInBackground) await Task.Delay(2000);
    }

    private void HookShellMessages(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        var taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        HwndSource.FromHwnd(helper.Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if ((uint)msg == taskbarCreated)
            {
                // Explorer restarted: the old desktop windows are gone.
                Log.Write("Explorer restarted, reattaching wallpapers");
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1500);
                    await AppState.Current.Engine.RestartAllAsync(reinitDesktop: true);
                });
            }
            return IntPtr.Zero;
        });
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _displayDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _displayDebounce.Stop();
            _displayDebounce.Tick -= DisplayTick;
            _displayDebounce.Tick += DisplayTick;
            _displayDebounce.Start();
        });
    }

    private async void DisplayTick(object? sender, EventArgs e)
    {
        _displayDebounce?.Stop();
        Log.Write("Display settings changed, restarting players");
        await AppState.Current.Engine.RestartAllAsync(reinitDesktop: true);
        (_window?.Pages.Screens)?.Refresh();
    }

    public void ShowMain()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
    }

    public void TrayHint() => _tray?.ShowHint(Strings.T("tray.hint.title"), Strings.T("tray.hint.body"));

    public void ExitApp()
    {
        Log.Write("Exiting");
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _autoPause?.Dispose();
        AppState.Current?.Engine.Dispose();
        _tray?.Dispose();
        _window?.CloseForReal();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppState.Current?.Engine.Dispose(); } catch { }
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();
        base.OnExit(e);
    }
}
