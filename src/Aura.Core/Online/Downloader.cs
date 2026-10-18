using Aura.Core.Library;

namespace Aura.Core.Online;

/// <summary>Downloads an online item into the library folder, with its card thumbnail next to it.</summary>
public static class Downloader
{
    public static async Task<string> DownloadAsync(
        OnlineItem item, string libraryFolder, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(libraryFolder);

        using var resp = await Http.Client.GetAsync(item.FullUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var ext = GuessExtension(resp, item);
        var target = UniquePath(Path.Combine(libraryFolder, SafeName(item.Title) + ext));
        var partial = target + ".part";

        var total = resp.Content.Headers.ContentLength;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(partial))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            var lastReport = 0.0;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total is > 0)
                {
                    var p = (double)done / total.Value;
                    if (p - lastReport >= 0.01) { lastReport = p; progress?.Report(p); }
                }
            }
        }
        File.Move(partial, target, overwrite: true);
        progress?.Report(1);

        // Keep the catalog thumbnail so the library card shows instantly.
        try
        {
            var bytes = await Http.Client.GetByteArrayAsync(item.ThumbUrl, ct).ConfigureAwait(false);
            var thumb = LibraryScanner.SidecarThumbPath(target);
            var thumbDir = Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
            thumbDir.Attributes |= FileAttributes.Hidden;
            await File.WriteAllBytesAsync(thumb, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* thumbnail is optional */ }

        return target;
    }

    private static string GuessExtension(HttpResponseMessage resp, OnlineItem item)
    {
        var fromHeader = resp.Content.Headers.ContentDisposition?.FileNameStar
                         ?? resp.Content.Headers.ContentDisposition?.FileName;
        var ext = Path.GetExtension(fromHeader?.Trim('"') ?? "");
        if (string.IsNullOrEmpty(ext))
        {
            var path = new Uri(resp.RequestMessage?.RequestUri?.ToString() ?? item.FullUrl).AbsolutePath;
            ext = Path.GetExtension(path);
        }
        if (string.IsNullOrEmpty(ext) || ext.Length > 5)
        {
            ext = resp.Content.Headers.ContentType?.MediaType switch
            {
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => item.IsVideo ? ".mp4" : ".jpg",
            };
        }
        return ext.ToLowerInvariant();
    }

    public static string SafeName(string title)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var clean = new string(title.Select(c => bad.Contains(c) ? ' ' : c).ToArray()).Trim();
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > 80) clean = clean[..80].Trim();
        return clean.Length == 0 ? "wallpaper" : clean;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
