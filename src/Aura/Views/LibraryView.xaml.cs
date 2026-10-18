using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Aura.Core;
using Aura.Core.Library;
using Aura.Engine;
using Microsoft.Win32;

namespace Aura.Views;

public partial class LibraryView : UserControl, IPage
{
    public static readonly DependencyProperty CardSlotProperty =
        DependencyProperty.Register(nameof(CardSlot), typeof(double), typeof(LibraryView), new PropertyMetadata(276.0));

    public static readonly DependencyProperty CardImageHeightProperty =
        DependencyProperty.Register(nameof(CardImageHeight), typeof(double), typeof(LibraryView), new PropertyMetadata(146.0));

    public double CardSlot { get => (double)GetValue(CardSlotProperty); set => SetValue(CardSlotProperty, value); }
    public double CardImageHeight { get => (double)GetValue(CardImageHeightProperty); set => SetValue(CardImageHeightProperty, value); }

    private readonly ObservableCollection<LibraryCard> _visible = new();
    private List<LibraryCard> _all = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reloadDebounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private bool _loaded;

    public LibraryView()
    {
        InitializeComponent();
        Cards.ItemsSource = _visible;

        TitleText.Text = Strings.T("lib.title");
        SearchBox.Tag = Strings.T("lib.search");
        FilterAll.Content = Strings.T("lib.all");
        FilterVideos.Content = Strings.T("lib.videos");
        FilterImages.Content = Strings.T("lib.images");
        AddLabel.Text = Strings.T("lib.add");
        OpenFolderText.Text = Strings.T("lib.openfolder");
        TargetLabel.Text = Strings.T("lib.target");
        EmptyTitle.Text = Strings.T("lib.empty.title");
        EmptyBody.Text = Strings.T("lib.empty.body");
        EmptyButton.Content = Strings.T("lib.empty.cta");

        _reloadDebounce.Tick += async (_, _) => { _reloadDebounce.Stop(); await ReloadAsync(); };

        var state = AppState.Current;
        state.LibraryChanged += () => Dispatcher.BeginInvoke(async () => await ReloadAsync());
        state.Engine.Changed += () => Dispatcher.BeginInvoke(MarkActive);
    }

    public async void OnShown()
    {
        FillTargets();
        if (!_loaded)
        {
            _loaded = true;
            WatchFolder();
            await ReloadAsync();
        }
        else
        {
            MarkActive();
        }
    }

    private void FillTargets()
    {
        var state = AppState.Current;
        var screens = WallpaperEngine.Screens();
        TargetBox.SelectionChanged -= Target_Changed;
        TargetBox.Items.Clear();
        TargetBox.Items.Add(new ComboBoxItem { Content = Strings.T("lib.target.all"), Tag = null });
        foreach (var s in screens)
        {
            var label = Strings.F("scr.name", s.Number) + (s.IsPrimary ? " · " + Strings.T("scr.primary") : "");
            TargetBox.Items.Add(new ComboBoxItem { Content = label, Tag = s.Device });
        }
        var index = screens.ToList().FindIndex(s => s.Device == state.TargetDevice);
        TargetBox.SelectedIndex = index < 0 ? 0 : index + 1;
        if (index < 0) state.TargetDevice = null;
        TargetBox.SelectionChanged += Target_Changed;

        // A single monitor needs no choice.
        TargetBox.Visibility = TargetLabel.Visibility = screens.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Target_Changed(object sender, SelectionChangedEventArgs e) =>
        AppState.Current.TargetDevice = (TargetBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void WatchFolder()
    {
        _watcher?.Dispose();
        var folder = AppState.Current.Settings.LibraryFolder;
        if (!Directory.Exists(folder)) return;
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler h = (_, e) =>
        {
            if (e.FullPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return;
            Dispatcher.BeginInvoke(() => { _reloadDebounce.Stop(); _reloadDebounce.Start(); });
        };
        _watcher.Created += h;
        _watcher.Deleted += h;
        _watcher.Renamed += (s, e) => h(s, e);
    }

    public void FolderChanged()
    {
        WatchFolder();
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var folder = AppState.Current.Settings.LibraryFolder;
        var items = await Task.Run(() => LibraryScanner.Scan(folder));

        // Keep already loaded thumbnails when the same file is still there.
        var old = _all.ToDictionary(c => c.Item.Path, StringComparer.OrdinalIgnoreCase);
        _all = items.Select(i => old.TryGetValue(i.Path, out var c) && c.Item.Modified == i.Modified ? c : new LibraryCard(i)).ToList();

        SubtitleText.Text = Strings.F("lib.subtitle", _all.Count, folder);
        MarkActive();
        ApplyFilter();

        foreach (var card in _all.Where(c => c.Thumb is null).ToList())
            card.Thumb = await Thumbnails.ForLibraryAsync(card.Item);
    }

    private void MarkActive()
    {
        var engine = AppState.Current.Engine;
        foreach (var c in _all) c.IsActive = engine.IsWallpaperFile(c.Item.Path);
    }

    private void ApplyFilter()
    {
        var q = SearchBox.Text.Trim();
        IEnumerable<LibraryCard> list = _all;
        if (FilterVideos.IsChecked == true) list = list.Where(c => c.Item.IsVideo);
        if (FilterImages.IsChecked == true) list = list.Where(c => !c.Item.IsVideo);
        if (q.Length > 0) list = list.Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase));

        _visible.Clear();
        foreach (var c in list) _visible.Add(c);
        EmptyState.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void Filter_Checked(object sender, RoutedEventArgs e) { if (IsLoaded) ApplyFilter(); }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Fill the row: as many ~260px cards as fit, all the same width.
        const double gap = 16, min = 250;
        var width = Math.Max(0, Scroller.ActualWidth - 24);
        var cols = Math.Max(1, (int)((width + gap) / (min + gap)));
        CardSlot = Math.Floor((width + gap) / cols);
        CardImageHeight = Math.Round((CardSlot - gap) * 9 / 16);
    }

    private async void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LibraryCard card)
            await ApplyAsync(card, AppState.Current.TargetDevice);
    }

    private static async Task ApplyAsync(LibraryCard card, string? device)
    {
        await AppState.Current.Engine.ApplyAsync(card.Item.Path, device);
    }

    private void Card_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryCard card, ContextMenu: { } menu }) return;
        menu.Items.Clear();

        var screens = WallpaperEngine.Screens();
        menu.Items.Add(Item(screens.Count > 1 ? Strings.T("lib.applyall") : Strings.T("lib.apply"),
            async () => await ApplyAsync(card, null)));
        if (screens.Count > 1)
        {
            foreach (var s in screens)
            {
                var device = s.Device;
                menu.Items.Add(Item(Strings.F("lib.applyon", Strings.F("scr.name", s.Number)),
                    async () => await ApplyAsync(card, device)));
            }
        }
        menu.Items.Add(new Separator { Background = (System.Windows.Media.Brush)FindResource("Line") });
        menu.Items.Add(Item(Strings.T("lib.reveal"), () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{card.Item.Path}\"") { UseShellExecute = true })));
        menu.Items.Add(Item(Strings.T("lib.delete"), () => Delete(card)));
    }

    private static MenuItem Item(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    private async void Delete(LibraryCard card)
    {
        var ok = MessageBox.Show(Window.GetWindow(this)!, Strings.F("lib.delete.confirm", card.Title), "Aura",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;

        var engine = AppState.Current.Engine;
        foreach (var s in WallpaperEngine.Screens())
            if (string.Equals(engine.WallpaperOn(s.Device), card.Item.Path, StringComparison.OrdinalIgnoreCase))
                await engine.ClearAsync(s.Device);

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(card.Item.Path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            var thumb = LibraryScanner.SidecarThumbPath(card.Item.Path);
            if (File.Exists(thumb)) File.Delete(thumb);
        }
        catch (Exception ex)
        {
            Log.Error("delete", ex);
        }
        await ReloadAsync();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var exts = string.Join(";", LibraryScanner.VideoExtensions.Concat(LibraryScanner.ImageExtensions).Select(x => "*" + x));
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = $"{Strings.T("lib.dialog.filter")}|{exts}",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        var folder = AppState.Current.Settings.LibraryFolder;
        Directory.CreateDirectory(folder);
        await Task.Run(() =>
        {
            foreach (var f in dlg.FileNames)
            {
                var dest = Path.Combine(folder, Path.GetFileName(f));
                if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Copy(f, dest, overwrite: false); }
                catch (IOException) { /* already there */ }
            }
        });
        await ReloadAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = AppState.Current.Settings.LibraryFolder;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void GoExplore_Click(object sender, RoutedEventArgs e) => AppState.Current.Navigate("explore");
}
