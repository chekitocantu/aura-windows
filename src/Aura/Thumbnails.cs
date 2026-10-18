using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aura.Core.Library;
using Aura.Core.Online;

namespace Aura;

/// <summary>Card pictures: catalog thumbnails from the web, and frames grabbed from local videos with mpv.</summary>
internal static class Thumbnails
{
    private static readonly SemaphoreSlim VideoGate = new(2, 2);
    private static readonly SemaphoreSlim WebGate = new(6, 6);

    public static async Task<ImageSource?> ForLibraryAsync(LibraryItem item)
    {
        try
        {
            var path = await LibraryThumbPathAsync(item);
            return path is null ? null : await LoadAsync(path, 480);
        }
        catch (Exception ex)
        {
            Log.Write($"Thumbnail failed for {item.Path}: {ex.Message}");
            return null;
        }
    }

    public static async Task<ImageSource?> ForUrlAsync(string url)
    {
        try
        {
            var file = Path.Combine(AppPaths.ThumbCache, "web-" + Hash(url) + ".jpg");
            if (!File.Exists(file))
            {
                await WebGate.WaitAsync();
                try
                {
                    if (!File.Exists(file))
                    {
                        var bytes = await Http.Client.GetByteArrayAsync(url);
                        Directory.CreateDirectory(AppPaths.ThumbCache);
                        await File.WriteAllBytesAsync(file, bytes);
                    }
                }
                finally { WebGate.Release(); }
            }
            return await LoadAsync(file, 480);
        }
        catch (Exception ex)
        {
            Log.Write($"Web thumbnail failed {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> LibraryThumbPathAsync(LibraryItem item)
    {
        var sidecar = LibraryScanner.SidecarThumbPath(item.Path);
        if (File.Exists(sidecar)) return sidecar;
        if (!item.IsVideo) return item.Path;

        var cached = Path.Combine(AppPaths.ThumbCache, "lib-" + Hash(item.Path + "|" + item.Modified.Ticks) + ".jpg");
        if (File.Exists(cached)) return cached;

        await VideoGate.WaitAsync();
        try
        {
            if (File.Exists(cached)) return cached;
            return await GrabFrameAsync(item.Path, cached) ? cached : null;
        }
        finally { VideoGate.Release(); }
    }

    /// <summary>Asks mpv for one frame a fifth of the way into the video.</summary>
    private static async Task<bool> GrabFrameAsync(string video, string output)
    {
        if (!File.Exists(AppPaths.MpvExe)) return false;
        var outDir = Path.Combine(AppPaths.Cache, "grab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var psi = new ProcessStartInfo(AppPaths.MpvExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[]
                     {
                         "--no-config", "--terminal=no", "--no-audio", "--start=20%", "--frames=1",
                         "--hr-seek=no", "--vo=image", "--vo-image-format=jpg", "--vo-image-jpeg-quality=85",
                         "--vf=scale=480:-2", "--vo-image-outdir=" + outDir, "--", video,
                     })
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(); } catch { } return false; }

            var frame = Directory.GetFiles(outDir, "*.jpg").FirstOrDefault();
            if (frame is null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Move(frame, output, overwrite: true);
            return true;
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    public static Task<ImageSource?> LoadAsync(string file, int decodeWidth) => Task.Run<ImageSource?>(() =>
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Write($"Decode failed {file}: {ex.Message}");
            return null;
        }
    });

    private static string Hash(string s) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..20].ToLowerInvariant();
}
