using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aura.Core.Online;

/// <summary>DenverCoder1's minimalistic wallpaper collection on GitHub (still images).</summary>
public static class Minimalistic
{
    private const string CatalogUrl =
        "https://raw.githubusercontent.com/DenverCoder1/minimalistic-wallpaper-collection/main/api/generated/images.json";
    private const int PerPage = 30;

    private static List<OnlineItem>? _all;

    public static async Task<IReadOnlyList<OnlineItem>> FetchAsync(OnlineQuery q, string cacheDir, CancellationToken ct)
    {
        if (_all is null)
        {
            var file = Path.Combine(cacheDir, "minimalistic.json");
            string json;
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(1))
            {
                json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            }
            else
            {
                json = await Http.GetStringAsync(CatalogUrl, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
                Directory.CreateDirectory(cacheDir);
                await File.WriteAllTextAsync(file, json, ct).ConfigureAwait(false);
            }
            _all = Parse(json).ToList();
        }

        IEnumerable<OnlineItem> list = _all;
        if (!string.IsNullOrWhiteSpace(q.Search))
            list = list.Where(i => i.Title.Contains(q.Search.Trim(), StringComparison.OrdinalIgnoreCase));
        return list.Skip((Math.Max(1, q.Page) - 1) * PerPage).Take(PerPage).ToList();
    }

    public static IReadOnlyList<OnlineItem> Parse(string json)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json) ?? [];
        return entries
            .Where(e => !string.IsNullOrEmpty(e.DownloadUrl) && IsImage(e.Name))
            .Select(e => new OnlineItem(
                Id: Path.GetFileNameWithoutExtension(e.Name),
                Title: TitleFromName(e.Name),
                Author: "Minimalistic Collection",
                ThumbUrl: $"https://wsrv.nl/?url={WebUtility.UrlEncode(e.DownloadUrl)}&w=400&h=225&fit=cover&output=jpg&q=80",
                FullUrl: e.DownloadUrl!,
                Resolution: e.Size is long s ? $"{s / 1024 / 1024.0:0.0} MB" : "",
                Source: OnlineSource.Minimalistic,
                IsVideo: false))
            .ToList();
    }

    private static bool IsImage(string name) =>
        Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    public static string TitleFromName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name).Replace('_', ' ').Replace('-', ' ');
        var words = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(' ', words).ToLowerInvariant());
    }

    private sealed class Entry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("size")] public long? Size { get; set; }
    }
}
