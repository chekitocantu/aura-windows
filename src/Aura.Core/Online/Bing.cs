using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aura.Core.Online;

/// <summary>Bing's daily photos. Page 1 is the last 16 days; later pages come from a public archive.</summary>
public static class Bing
{
    private const string Daily1 = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n=8&mkt=en-US";
    private const string Daily2 = "https://www.bing.com/HPImageArchive.aspx?format=js&idx=8&n=8&mkt=en-US";
    private const string ArchiveUrl = "https://raw.githubusercontent.com/zkeq/Bing-Wallpaper-Action/main/data/en-US_all.json";
    private const int PerPage = 24;

    private static List<Image>? _archive;

    public static async Task<IReadOnlyList<OnlineItem>> FetchAsync(OnlineQuery q, string cacheDir, CancellationToken ct)
    {
        if (q.Page <= 1)
        {
            var t1 = Http.GetStringAsync(Daily1, TimeSpan.FromSeconds(12), ct);
            var t2 = Http.GetStringAsync(Daily2, TimeSpan.FromSeconds(12), ct);
            var items = new List<OnlineItem>();
            foreach (var t in new[] { t1, t2 })
            {
                try { items.AddRange(ParseDaily(await t.ConfigureAwait(false))); }
                catch (Exception) when (!ct.IsCancellationRequested) { /* one page may fail */ }
            }
            return items.DistinctBy(i => i.Id).ToList();
        }

        _archive ??= await LoadArchiveAsync(cacheDir, ct).ConfigureAwait(false);
        return _archive.Skip((q.Page - 2) * PerPage).Take(PerPage)
            .Select(Convert).OfType<OnlineItem>().ToList();
    }

    private static async Task<List<Image>> LoadArchiveAsync(string cacheDir, CancellationToken ct)
    {
        var file = Path.Combine(cacheDir, "bing_archive.json");
        string json;
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(3))
        {
            json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
        }
        else
        {
            json = await Http.GetStringAsync(ArchiveUrl, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(file, json, ct).ConfigureAwait(false);
        }
        return JsonSerializer.Deserialize<ArchiveRoot>(json)?.Data ?? [];
    }

    public static IReadOnlyList<OnlineItem> ParseDaily(string json) =>
        (JsonSerializer.Deserialize<DailyRoot>(json)?.Images ?? [])
            .Select(Convert).OfType<OnlineItem>().ToList();

    public static IReadOnlyList<OnlineItem> ParseArchive(string json) =>
        (JsonSerializer.Deserialize<ArchiveRoot>(json)?.Data ?? [])
            .Select(Convert).OfType<OnlineItem>().ToList();

    private static OnlineItem? Convert(Image img)
    {
        if (string.IsNullOrEmpty(img.UrlBase)) return null;
        var id = img.Hash ?? img.UrlBase.Replace("/th?id=", "");
        var title = string.IsNullOrWhiteSpace(img.Title) ? TitleFromUrl(img.UrlBase) : img.Title.Trim();
        var date = img.StartDate is { Length: 8 } d ? $"{d[..4]}-{d[4..6]}-{d[6..]}" : "";
        return new OnlineItem(
            Id: id,
            Title: title,
            Author: img.Copyright ?? "Microsoft Bing",
            ThumbUrl: $"https://www.bing.com{img.UrlBase}_1920x1080.jpg&w=480&h=270&rs=1&c=4",
            FullUrl: $"https://www.bing.com{img.UrlBase}_UHD.jpg",
            Resolution: date.Length > 0 ? $"4K · {date}" : "4K",
            Source: OnlineSource.Bing,
            IsVideo: false);
    }

    private static string TitleFromUrl(string urlBase)
    {
        // "/th?id=OHR.MountFuji_EN-US123" -> "Mount Fuji"
        var i = urlBase.IndexOf("OHR.", StringComparison.Ordinal);
        if (i < 0) return "Bing";
        var core = urlBase[(i + 4)..].Split('_')[0];
        var sb = new System.Text.StringBuilder();
        for (var k = 0; k < core.Length; k++)
        {
            if (k > 0 && char.IsUpper(core[k]) && char.IsLower(core[k - 1])) sb.Append(' ');
            sb.Append(core[k]);
        }
        return sb.ToString();
    }

    private sealed class DailyRoot
    {
        [JsonPropertyName("images")] public List<Image>? Images { get; set; }
    }

    private sealed class ArchiveRoot
    {
        [JsonPropertyName("data")] public List<Image>? Data { get; set; }
    }

    private sealed class Image
    {
        [JsonPropertyName("urlbase")] public string? UrlBase { get; set; }
        [JsonPropertyName("copyright")] public string? Copyright { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("startdate")] public string? StartDate { get; set; }
        [JsonPropertyName("hsh")] public string? Hash { get; set; }
    }
}
