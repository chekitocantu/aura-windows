using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aura.Core;
using Aura.Core.Online;
using Aura.Engine;

namespace Aura;

/// <summary>
/// `Aura.exe --selftest <folder>`: used by CI on real Windows. Downloads a live wallpaper, puts it
/// behind the icons, checks where the window landed, takes screenshots of the desktop and every
/// page, and writes report.txt. Exit code 0 means every check passed.
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(AppState state, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new StringBuilder();
        var failures = new List<string>();
        void Line(string s) { report.AppendLine(s); Log.Write("[selftest] " + s); }
        void Check(bool ok, string what) { Line((ok ? "PASS " : "FAIL ") + what); if (!ok) failures.Add(what); }

        try
        {
            state.Settings.LibraryFolder = Path.Combine(outDir, "library");
            state.Settings.Mute = true;
            Directory.CreateDirectory(state.Settings.LibraryFolder);

            Line($"OS {Environment.OSVersion} build {Environment.OSVersion.Version.Build}");
            foreach (var s in WallpaperEngine.Screens()) Line($"Screen {s.Number} {s.Device} {s.Bounds} primary={s.IsPrimary}");
            Check(File.Exists(AppPaths.MpvExe), $"mpv present at {AppPaths.MpvExe}");

            var layer = state.Engine.Layer;
            Check(layer.Initialize(), $"desktop layer found (raised={layer.IsRaised})");

            // 1. Every catalog answers.
            OnlineItem? video = null, image = null;
            foreach (var src in Enum.GetValues<OnlineSource>())
            {
                try
                {
                    var items = await Catalog.FetchAsync(new OnlineQuery(src, VideoQuality: "hd"), AppPaths.Cache, CancellationToken.None);
                    Check(items.Count > 0, $"catalog {src} returned {items.Count} items");
                    if (src == OnlineSource.MotionBgs) video = items.FirstOrDefault();
                    if (src == OnlineSource.Bing) image = items.FirstOrDefault();
                }
                catch (Exception ex) { Check(false, $"catalog {src}: {ex.Message}"); }
            }

            // 2. Download and apply a live wallpaper on every monitor.
            if (video is not null)
            {
                var file = await Downloader.DownloadAsync(video, state.Settings.LibraryFolder, null, CancellationToken.None);
                Check(new FileInfo(file).Length > 100_000, $"downloaded {Path.GetFileName(file)} ({new FileInfo(file).Length / 1024} KB)");
                await state.Engine.ApplyAsync(file, null);
                await Task.Delay(6000);

                var diag = state.Engine.Diagnostics().ToList();
                Check(diag.Count == WallpaperEngine.Screens().Count, $"{diag.Count} player(s) running");
                foreach (var (device, window, parent) in diag)
                {
                    Native.GetWindowRect(window, out var r);
                    var expected = layer.IsRaised ? layer.Progman : layer.WorkerW;
                    Line($"Player {device}: hwnd={window} parent={parent} ({Native.ClassName(parent)}) rect={r}");
                    Check(parent == expected, $"player on {device} is a child of the desktop layer");
                    if (layer.IsRaised)
                    {
                        // Must sit below the icons (DefView earlier in z-order) and above Windows' WorkerW.
                        var order = ZOrder(layer.Progman);
                        var iDef = order.IndexOf(layer.DefView);
                        var iMe = order.IndexOf(window);
                        var iWorker = order.IndexOf(layer.WorkerW);
                        Line($"Progman children z-order: {string.Join(", ", order.Select(h => $"{h}:{Native.ClassName(h)}"))}");
                        Check(iDef >= 0 && iMe > iDef && (iWorker < 0 || iMe < iWorker), "player is between the icons and the WorkerW");
                    }
                }
                Shot(Path.Combine(outDir, "01-desktop-video.png"));

                state.Engine.SetUserPaused(true);
                await Task.Delay(1500);
                Shot(Path.Combine(outDir, "02-desktop-paused.png"));
                state.Engine.SetUserPaused(false);
                Check(!state.Engine.UserPaused, "pause and resume");
            }

            // 3. A still image goes through the normal Windows wallpaper.
            if (image is not null)
            {
                var file = await Downloader.DownloadAsync(image, state.Settings.LibraryFolder, null, CancellationToken.None);
                var primary = WallpaperEngine.Screens().First();
                await state.Engine.ApplyAsync(file, primary.Device);
                await Task.Delay(3000);
                Check(!state.Engine.IsPlaying(primary.Device), "image replaced the video on the primary screen");
                Shot(Path.Combine(outDir, "03-desktop-image.png"));
            }

            // 4. The window and every page render.
            var win = new MainWindow { Width = 1280, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 };
            win.Show();
            if (video is not null)
            {
                var again = Directory.GetFiles(state.Settings.LibraryFolder, "*.mp4").First();
                await state.Engine.ApplyAsync(again, null);
            }
            foreach (var page in new[] { "library", "explore", "screens", "settings" })
            {
                win.Navigate(page);
                await Task.Delay(page == "explore" ? 9000 : 4000);
                Render(win, Path.Combine(outDir, $"page-{page}.png"));
            }
            Shot(Path.Combine(outDir, "04-desktop-with-window.png"));
            win.CloseForReal();
        }
        catch (Exception ex)
        {
            Check(false, "unexpected error: " + ex);
        }

        Line(failures.Count == 0 ? "RESULT: ALL PASSED" : $"RESULT: {failures.Count} FAILED");
        await File.WriteAllTextAsync(Path.Combine(outDir, "report.txt"), report.ToString());
        return failures.Count == 0 ? 0 : 1;
    }

    private static List<IntPtr> ZOrder(IntPtr parent)
    {
        var list = new List<IntPtr>();
        for (var h = Native.GetWindow(parent, Native.GW_CHILD); h != IntPtr.Zero; h = Native.GetWindow(h, 2 /* GW_HWNDNEXT */))
            list.Add(h);
        return list;
    }

    private static void Shot(string file)
    {
        try
        {
            var b = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left, b.Top, 0, 0, b.Size);
            bmp.Save(file, ImageFormat.Png);
        }
        catch (Exception ex) { Log.Error("screenshot", ex); }
    }

    private static void Render(Window w, string file)
    {
        try
        {
            w.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(w);
            var content = (FrameworkElement)w.Content;
            var rtb = new RenderTargetBitmap((int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(file);
            enc.Save(fs);
        }
        catch (Exception ex) { Log.Error("render", ex); }
    }
}
