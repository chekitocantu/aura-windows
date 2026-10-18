using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aura.Core;
using Aura.Core.Library;
using Aura.Core.Online;

namespace Aura.Views;

public partial class ExploreView : UserControl, IPage
{
    public static readonly DependencyProperty CardSlotProperty =
        DependencyProperty.Register(nameof(CardSlot), typeof(double), typeof(ExploreView), new PropertyMetadata(276.0));

    public static readonly DependencyProperty CardImageHeightProperty =
        DependencyProperty.Register(nameof(CardImageHeight), typeof(double), typeof(ExploreView), new PropertyMetadata(146.0));

    public double CardSlot { get => (double)GetValue(CardSlotProperty); set => SetValue(CardSlotProperty, value); }
    public double CardImageHeight { get => (double)GetValue(CardImageHeightProperty); set => SetValue(CardImageHeightProperty, value); }

    private readonly ObservableCollection<OnlineCard> _cards = new();
    private OnlineSource _source = OnlineSource.MotionBgs;
    private string _category = "all";
    private int _page;
    private bool _loading;
    private bool _endReached;
    private CancellationTokenSource? _cts;
    private bool _started;

    // Remembers what was downloaded this session, keyed by item, so cards keep their state.
    private static readonly Dictionary<string, string> Downloaded = new();

    public ExploreView()
    {
        InitializeComponent();
        Cards.ItemsSource = _cards;

        TitleText.Text = Strings.T("exp.title");
        SubtitleText.Text = Strings.T("exp.subtitle");
        SrcMotion.Content = Strings.T("exp.src.motionbgs");
        SrcWallhaven.Content = Strings.T("exp.src.wallhaven");
        SrcBing.Content = Strings.T("exp.src.bing");
        SrcMinimal.Content = Strings.T("exp.src.minimalistic");
        SearchBox.Tag = Strings.T("exp.search");
        SearchText.Text = Strings.T("exp.go");
        MoreButton.Content = Strings.T("exp.more");

        foreach (var s in Wallhaven.Sortings) SortBox.Items.Add(new ComboBoxItem { Content = Strings.T("sort." + s), Tag = s });
        foreach (var r in Wallhaven.Resolutions) ResBox.Items.Add(new ComboBoxItem { Content = Strings.T("res." + r), Tag = r });
        SortBox.SelectedIndex = 0;
        ResBox.SelectedIndex = 0;
    }

    public void OnShown()
    {
        if (_started) return;
        _started = true;
        SrcMotion.IsChecked = true; // triggers the first load
    }

    private void Source_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !Enum.TryParse(tag, out OnlineSource src)) return;
        _source = src;
        _category = "all";
        SearchBox.Text = "";
        SearchRow.Visibility = Catalog.HasSearch(src) ? Visibility.Visible : Visibility.Collapsed;
        WallhavenOptions.Visibility = src == OnlineSource.Wallhaven ? Visibility.Visible : Visibility.Collapsed;
        BuildCategories();
        _ = ReloadAsync();
    }

    private void BuildCategories()
    {
        CategoryBar.Children.Clear();
        var cats = _source switch
        {
            OnlineSource.MotionBgs => MotionBgs.Categories,
            OnlineSource.Wallhaven => Wallhaven.Categories,
            _ => Array.Empty<string>(),
        };
        CategoryBar.Visibility = cats.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in cats)
        {
            var rb = new RadioButton
            {
                Content = Strings.T("cat." + c),
                Tag = c,
                GroupName = "cat",
                Style = (Style)FindResource("Pill"),
                IsChecked = c == _category,
            };
            rb.Checked += (_, _) =>
            {
                _category = (string)rb.Tag;
                // A category replaces a running search, like on the website.
                SearchBox.Text = "";
                _ = ReloadAsync();
            };
            CategoryBar.Children.Add(rb);
        }
    }

    private void Option_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_started && IsLoaded) _ = ReloadAsync();
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = ReloadAsync();
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private async void More_Click(object sender, RoutedEventArgs e) => await LoadPageAsync();

    private async Task ReloadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _cards.Clear();
        _page = 0;
        _endReached = false;
        _loading = false;
        Scroller.ScrollToTop();
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (_loading || _endReached) return;
        _loading = true;
        var ct = _cts?.Token ?? CancellationToken.None;
        MoreButton.Visibility = Visibility.Collapsed;
        StatusText.Text = Strings.T("exp.loading");
        StatusText.Visibility = Visibility.Visible;

        var s = AppState.Current.Settings;
        var query = new OnlineQuery(
            _source,
            _page + 1,
            string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
            _category,
            (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "toplist",
            (ResBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all",
            s.VideoQuality);

        try
        {
            var items = await Catalog.FetchAsync(query, AppPaths.Cache, ct);
            if (ct.IsCancellationRequested) return;
            _page++;

            var known = _cards.Select(c => c.Item.Key).ToHashSet();
            var fresh = items.Where(i => known.Add(i.Key)).Select(i => new OnlineCard(i, LocalCopy(i))).ToList();
            foreach (var c in fresh) _cards.Add(c);

            _endReached = items.Count == 0 || fresh.Count == 0;
            StatusText.Text = _cards.Count == 0 ? Strings.T("exp.noresults") : "";
            StatusText.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreButton.Visibility = _endReached ? Visibility.Collapsed : Visibility.Visible;
            MoreButton.Content = Strings.T("exp.more");

            await Task.WhenAll(fresh.Select(async c => c.Thumb = await Thumbnails.ForUrlAsync(c.Item.ThumbUrl)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error($"explore {query}", ex);
            StatusText.Text = Strings.F("exp.error", ex.Message);
            StatusText.Visibility = Visibility.Visible;
            MoreButton.Content = Strings.T("exp.retry");
            MoreButton.Visibility = Visibility.Visible;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string? LocalCopy(OnlineItem item)
    {
        if (Downloaded.TryGetValue(item.Key, out var path) && File.Exists(path)) return path;

        // Downloaded in an earlier run: the file is named after the title.
        var folder = AppState.Current.Settings.LibraryFolder;
        var stem = Downloader.SafeName(item.Title);
        try
        {
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, stem + ".*").FirstOrDefault(LibraryScanner.IsSupported)
                : null;
        }
        catch (IOException) { return null; }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OnlineCard card) await DownloadAsync(card, apply: false);
    }

    private async void DownloadApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not OnlineCard card) return;
        if (card.LocalPath is { } path) await AppState.Current.Engine.ApplyAsync(path, AppState.Current.TargetDevice);
        else await DownloadAsync(card, apply: true);
    }

    private async void ApplyLocal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is OnlineCard { LocalPath: { } path })
            await AppState.Current.Engine.ApplyAsync(path, AppState.Current.TargetDevice);
    }

    internal static async Task<string?> DownloadAsync(OnlineCard card, bool apply)
    {
        if (card.IsDownloading) return null;
        card.IsDownloading = true;
        card.Progress = 0;
        var state = AppState.Current;
        try
        {
            var progress = new Progress<double>(p => card.Progress = p);
            var path = await Downloader.DownloadAsync(card.Item, state.Settings.LibraryFolder, progress, CancellationToken.None);
            Downloaded[card.Item.Key] = path;
            card.LocalPath = path;
            state.NotifyLibraryChanged();
            if (apply) await state.Engine.ApplyAsync(path, state.TargetDevice);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("download " + card.Item.FullUrl, ex);
            state.Toast(Strings.F("err.download", ex.Message));
            return null;
        }
        finally
        {
            card.IsDownloading = false;
        }
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double gap = 16, min = 250;
        var width = Math.Max(0, Scroller.ActualWidth - 24);
        var cols = Math.Max(1, (int)((width + gap) / (min + gap)));
        CardSlot = Math.Floor((width + gap) / cols);
        CardImageHeight = Math.Round((CardSlot - gap) * 9 / 16);
    }
}
