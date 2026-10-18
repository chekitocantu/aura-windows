using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Aura.Core;
using Aura.Core.Library;
using Aura.Engine;

namespace Aura.Views;

public partial class ScreensView : UserControl, IPage
{
    private const double MapWidth = 640, MapHeight = 230, MapGap = 10;

    public ScreensView()
    {
        InitializeComponent();
        TitleText.Text = Strings.T("scr.title");
        SubtitleText.Text = Strings.T("scr.subtitle");
        AppState.Current.Engine.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Refresh(); });
    }

    public void OnShown() => Refresh();

    public async void Refresh()
    {
        var engine = AppState.Current.Engine;
        var screens = WallpaperEngine.Screens();
        var cards = screens.Select(s => new ScreenCard(s, engine.WallpaperOn(s.Device))).ToList();

        // Scale the virtual desktop into the map box.
        var left = screens.Min(s => s.Bounds.Left);
        var top = screens.Min(s => s.Bounds.Top);
        var right = screens.Max(s => s.Bounds.Right);
        var bottom = screens.Max(s => s.Bounds.Bottom);
        var scale = Math.Min(MapWidth / Math.Max(1, right - left), MapHeight / Math.Max(1, bottom - top));
        foreach (var c in cards)
        {
            c.X = (c.Screen.Bounds.Left - left) * scale + MapGap / 2;
            c.Y = (c.Screen.Bounds.Top - top) * scale + MapGap / 2;
            c.W = Math.Max(20, c.Screen.Bounds.Width * scale - MapGap);
            c.H = Math.Max(20, c.Screen.Bounds.Height * scale - MapGap);
        }
        Map.Width = (right - left) * scale + MapGap;
        Map.Height = (bottom - top) * scale + MapGap;

        Map.ItemsSource = cards;
        Rows.ItemsSource = cards;

        foreach (var c in cards.Where(c => c.File is not null && File.Exists(c.File)))
        {
            var info = new FileInfo(c.File!);
            c.Thumb = await Thumbnails.ForLibraryAsync(new LibraryItem(c.File!, info.Name, LibraryScanner.IsVideo(c.File!),
                info.Length, info.LastWriteTimeUtc));
        }
    }

    private void Change_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScreenCard card) return;
        AppState.Current.TargetDevice = card.Screen.Device;
        AppState.Current.Navigate("library");
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ScreenCard card)
            await AppState.Current.Engine.ClearAsync(card.Screen.Device);
    }
}
