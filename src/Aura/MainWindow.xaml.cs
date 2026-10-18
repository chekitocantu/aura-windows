using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Aura.Core;
using Aura.Core.Library;
using Aura.Engine;
using Aura.Views;

namespace Aura;

internal sealed class Pages
{
    public LibraryView Library { get; } = new();
    public ExploreView Explore { get; } = new();
    public ScreensView Screens { get; } = new();
    public SettingsView Settings { get; } = new();
}

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _reallyClose;
    private string? _nowFile;

    internal Pages Pages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyTexts();

        var state = AppState.Current;
        state.Engine.Changed += () => Dispatcher.BeginInvoke(RefreshNowPlaying);
        state.ToastRequested += msg => Dispatcher.BeginInvoke(() => ShowToast(msg));
        state.NavigateRequested += page => Dispatcher.BeginInvoke(() => Navigate(page));
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

        SourceInitialized += (_, _) => DarkTitleBar();
        NavLibrary.IsChecked = true;
        RefreshNowPlaying();
    }

    private void ApplyTexts()
    {
        TagLine.Text = Strings.Language == "es" ? "para Windows" : "for Windows";
        NavLibraryText.Text = Strings.T("nav.library");
        NavExploreText.Text = Strings.T("nav.explore");
        NavScreensText.Text = Strings.T("nav.screens");
        NavSettingsText.Text = Strings.T("nav.settings");
    }

    private void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        // Windows 11: paint the caption the same color as the sidebar (0x00BBGGRR).
        var caption = 0x001A1110;
        Native.DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }

    public void Navigate(string page)
    {
        var target = page switch
        {
            "explore" => NavExplore,
            "screens" => NavScreens,
            "settings" => NavSettings,
            _ => NavLibrary,
        };
        if (target.IsChecked == true) Nav_Checked(target, new RoutedEventArgs());
        else target.IsChecked = true;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        UserControl page = tag switch
        {
            "explore" => Pages.Explore,
            "screens" => Pages.Screens,
            "settings" => Pages.Settings,
            _ => Pages.Library,
        };
        PageHost.Content = page;
        if (page is IPage p) p.OnShown();
    }

    private async void RefreshNowPlaying()
    {
        var state = AppState.Current;
        var engine = state.Engine;
        var screens = WallpaperEngine.Screens();
        var file = screens.Select(s => engine.WallpaperOn(s.Device)).FirstOrDefault(f => f is not null);

        NowLabel.Text = Strings.T("now.title");
        PauseButton.IsEnabled = engine.AnyVideo;
        StopButton.IsEnabled = file is not null;
        StopButton.ToolTip = Strings.T("now.stop");
        PauseText.Text = engine.UserPaused ? Strings.T("now.resume") : Strings.T("now.pause");
        PauseIcon.Text = engine.UserPaused ? "" : "";

        if (file is null)
        {
            NowTitle.Text = Strings.T("now.none");
            NowState.Visibility = Visibility.Collapsed;
        }
        else
        {
            NowTitle.Text = Path.GetFileNameWithoutExtension(file);
            var status = engine.UserPaused ? Strings.T("now.paused")
                : engine.AutoPaused ? Strings.T("now.autopaused")
                : null;
            NowState.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
            NowStateText.Text = status ?? "";
        }

        if (file != _nowFile)
        {
            _nowFile = file;
            NowThumb.ImageSource = null;
            if (file is not null && File.Exists(file))
            {
                var info = new FileInfo(file);
                var item = new LibraryItem(file, Path.GetFileNameWithoutExtension(file), LibraryScanner.IsVideo(file),
                    info.Length, info.LastWriteTimeUtc);
                var img = await Thumbnails.ForLibraryAsync(item);
                if (_nowFile == file) NowThumb.ImageSource = img;
            }
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var engine = AppState.Current.Engine;
        engine.SetUserPaused(!engine.UserPaused);
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) =>
        await AppState.Current.Engine.ClearAsync(null);

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_reallyClose)
        {
            base.OnClosing(e);
            return;
        }
        // Closing the window keeps the wallpaper running from the tray.
        e.Cancel = true;
        Hide();
        var s = AppState.Current.Settings;
        if (!s.TrayHintShown)
        {
            s.TrayHintShown = true;
            AppState.Current.Save();
            (Application.Current as App)?.TrayHint();
        }
    }

    public void CloseForReal()
    {
        _reallyClose = true;
        Close();
    }
}

/// <summary>Pages get told when they become visible so they can refresh.</summary>
internal interface IPage
{
    void OnShown();
}
