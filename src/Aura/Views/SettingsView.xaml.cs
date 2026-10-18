using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Aura.Core;
using Microsoft.Win32;

namespace Aura.Views;

public partial class SettingsView : UserControl, IPage
{
    private bool _ready;

    public SettingsView()
    {
        InitializeComponent();

        TitleText.Text = Strings.T("set.title");
        GeneralTitle.Text = Strings.T("set.general");
        LanguageLabel.Text = Strings.T("set.language");
        StartupBox.Content = Strings.T("set.startup");
        StartupHint.Text = Strings.T("set.startup.hint");
        FolderLabel.Text = Strings.T("set.folder");
        FolderButton.Content = Strings.T("set.folder.change");
        PlaybackTitle.Text = Strings.T("set.playback");
        FitLabel.Text = Strings.T("set.fit");
        QualityLabel.Text = Strings.T("set.quality");
        MuteBox.Content = Strings.T("set.mute");
        VolumeLabel.Text = Strings.T("set.volume");
        HwdecBox.Content = Strings.T("set.hwdec");
        HwdecHint.Text = Strings.T("set.hwdec.hint");
        PauseTitle.Text = Strings.T("set.pause");
        PauseFullscreenBox.Content = Strings.T("set.pause.fullscreen");
        PauseMaximizedBox.Content = Strings.T("set.pause.maximized");
        PauseBatteryBox.Content = Strings.T("set.pause.battery");
        PauseLockBox.Content = Strings.T("set.pause.lock");
        AboutTitle.Text = Strings.T("set.about");
        AboutText.Text = Strings.F("set.about.body", typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0");
        LogsButton.Content = Strings.T("set.logs");

        Add(LanguageBox, ("auto", Strings.T("set.language.auto")), ("es", "Español"), ("en", "English"));
        Add(FitBox, ("fill", Strings.T("fit.fill")), ("fit", Strings.T("fit.fit")), ("stretch", Strings.T("fit.stretch")));
        Add(QualityBox, ("4k", Strings.T("q.4k")), ("hd", Strings.T("q.hd")));
    }

    private static void Add(ComboBox box, params (string Tag, string Label)[] items)
    {
        foreach (var (tag, label) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
    }

    private static void Select(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if ((string)item.Tag == tag) { box.SelectedItem = item; return; }
        box.SelectedIndex = 0;
    }

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    public void OnShown()
    {
        _ready = false;
        var s = AppState.Current.Settings;
        Select(LanguageBox, s.Language);
        Select(FitBox, s.Fit);
        Select(QualityBox, s.VideoQuality);
        StartupBox.IsChecked = s.StartWithWindows;
        FolderPath.Text = s.LibraryFolder;
        MuteBox.IsChecked = s.Mute;
        VolumeSlider.Value = s.Volume;
        VolumeValue.Text = s.Volume.ToString();
        VolumeRow.IsEnabled = !s.Mute;
        VolumeRow.Opacity = s.Mute ? 0.4 : 1;
        HwdecBox.IsChecked = s.HardwareDecoding;
        PauseFullscreenBox.IsChecked = s.PauseOnFullscreen;
        PauseMaximizedBox.IsChecked = s.PauseOnMaximized;
        PauseBatteryBox.IsChecked = s.PauseOnBattery;
        PauseLockBox.IsChecked = s.PauseOnLock;
        _ready = true;
    }

    private static void Save() => AppState.Current.Save();

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || TagOf(LanguageBox) is not { } lang) return;
        AppState.Current.Settings.Language = lang;
        Save();
        // Texts are set once per window; reopen Aura to apply everywhere.
        var restart = Strings.Language == "es" || (lang == "es")
            ? "Reinicia Aura para cambiar el idioma. ¿Reiniciar ahora?"
            : "Restart Aura to change the language. Restart now?";
        if (MessageBox.Show(Window.GetWindow(this)!, restart, "Aura", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            var exe = Environment.ProcessPath;
            (Application.Current as App)?.ExitApp();
            if (exe is not null) Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = true });
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        var on = StartupBox.IsChecked == true;
        AppState.Current.Settings.StartWithWindows = on;
        AutoStart.Apply(on);
        Save();
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Current.Settings;
        var dlg = new OpenFolderDialog { InitialDirectory = Directory.Exists(s.LibraryFolder) ? s.LibraryFolder : null };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        s.LibraryFolder = dlg.FolderName;
        FolderPath.Text = s.LibraryFolder;
        Save();
        ((Window.GetWindow(this) as MainWindow)?.Pages.Library)?.FolderChanged();
    }

    private async void Fit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || TagOf(FitBox) is not { } fit) return;
        AppState.Current.Settings.Fit = fit;
        Save();
        await AppState.Current.Engine.RestartAllAsync();
    }

    private void Quality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || TagOf(QualityBox) is not { } q) return;
        AppState.Current.Settings.VideoQuality = q;
        Save();
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Current.Settings;
        s.Mute = MuteBox.IsChecked == true;
        VolumeRow.IsEnabled = !s.Mute;
        VolumeRow.Opacity = s.Mute ? 0.4 : 1;
        Save();
        AppState.Current.Engine.UpdateAudio();
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        var s = AppState.Current.Settings;
        s.Volume = (int)Math.Round(VolumeSlider.Value);
        VolumeValue.Text = s.Volume.ToString();
        Save();
        AppState.Current.Engine.UpdateAudio();
    }

    private async void Hwdec_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.Settings.HardwareDecoding = HwdecBox.IsChecked == true;
        Save();
        await AppState.Current.Engine.RestartAllAsync();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Current.Settings;
        s.PauseOnFullscreen = PauseFullscreenBox.IsChecked == true;
        s.PauseOnMaximized = PauseMaximizedBox.IsChecked == true;
        s.PauseOnBattery = PauseBatteryBox.IsChecked == true;
        s.PauseOnLock = PauseLockBox.IsChecked == true;
        Save();
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Log.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = true });
    }
}
