using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aura.Core.Online;

/// <summary>Still images from the public Wallhaven API (safe-for-work only).</summary>
public static class Wallhaven
{
    /// <summary>Wallhaven category bit masks: general / anime / people.</summary>
    public static readonly string[] Categories = ["all", "general", "anime"];
    public static readonly string[] Sortings = ["toplist", "hot", "random"];
    public static readonly string[] Resolutions = ["all", "4k", "2k", "ultrawide"];

    public static string BuildUrl(OnlineQuery q)
    {
        var mask = q.Category switch
        {
            "general" => "100",
            "anime" => "010",
            _ => "110",
        };
        var p = new List<string>
        {
            $"categories={mask}",
            "purity=100",
            $"sorting={q.Sorting}",
            $"page={Math.Max(1, q.Page)}",
        };
        switch (q.Resolution)
        {
            case "4k": p.Add("atleast=3840x2160"); break;
            case "2k": p.Add("resolutions=2560x1440"); break;
            case "ultrawide": p.Add("ratios=21x9,32x9"); p.Add("atleast=2560x1080"); break;
            default: p.Add("resolutions=1920x1080,2560x1440,3840x2160"); break;
        }
        if (q.Sorting == "toplist") p.Add("topRange=1M");
        if (!string.IsNullOrWhiteSpace(q.Search)) p.Add("q=" + WebUtility.UrlEncode(q.Search.Trim()));
        return "https://wallhaven.cc/api/v1/search?" + string.Join('&', p);
    }

    public static async Task<IReadOnlyList<OnlineItem>> FetchAsync(OnlineQuery q, CancellationToken ct)
    {
        var json = await Http.GetStringAsync(BuildUrl(q), TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
        return Parse(json);
    }

    public static IReadOnlyList<OnlineItem> Parse(string json)
    {
        var resp = JsonSerializer.Deserialize<Response>(json);
        var items = new List<OnlineItem>();
        foreach (var e in resp?.Data ?? [])
        {
            if (string.IsNullOrEmpty(e.Path)) continue;
            items.Add(new OnlineItem(
                Id: e.Id,
                Title: $"Wallhaven {e.Id}",
                Author: $"Wallhaven · {e.Category ?? "art"}",
                ThumbUrl: e.Thumbs?.Large ?? e.Thumbs?.Small ?? e.Path,
                FullUrl: e.Path,
                Resolution: e.Resolution ?? "",
                Source: OnlineSource.Wallhaven,
                IsVideo: false));
        }
        return items;
    }

    private sealed class Response
    {
        [JsonPropertyName("data")] public List<Entry>? Data { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("resolution")] public string? Resolution { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("thumbs")] public Thumbs? Thumbs { get; set; }
    }

    private sealed class Thumbs
    {
        [JsonPropertyName("large")] public string? Large { get; set; }
        [JsonPropertyName("small")] public string? Small { get; set; }
    }
}
