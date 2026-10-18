using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Aura.Core.Online;

/// <summary>Live (video) wallpapers from motionbgs.com. The site has no API, so the HTML is parsed.</summary>
public static partial class MotionBgs
{
    public static readonly string[] Categories =
        ["all", "anime", "games", "nature", "space", "car", "fantasy", "superhero", "technology"];

    public static string BuildUrl(int page, string? search, string category)
    {
        var q = search?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            var encoded = WebUtility.UrlEncode(q);
            return page <= 1
                ? $"https://motionbgs.com/search?q={encoded}"
                : $"https://motionbgs.com/search?q={encoded}&page={page}";
        }

        var cat = (category ?? "all").Trim().ToLowerInvariant();
        if (cat is "" or "all")
            return page <= 1 ? "https://motionbgs.com/" : $"https://motionbgs.com/{page}/";
        return page <= 1 ? $"https://motionbgs.com/tag:{cat}/" : $"https://motionbgs.com/tag:{cat}/{page}/";
    }

    public static async Task<IReadOnlyList<OnlineItem>> FetchAsync(OnlineQuery query, CancellationToken ct)
    {
        var html = await Http.GetStringAsync(BuildUrl(query.Page, query.Search, query.Category),
            TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        return Parse(html, query.VideoQuality);
    }

    // A card looks like: <a title="X live wallpaper" href=/slug> ... <img src=/i/c/364x205/media/9967/slug.3840x2160.jpg>
    [GeneratedRegex(@"/media/(\d+)/([^""'\s>]{1,160}?)\.jpg", RegexOptions.IgnoreCase)]
    private static partial Regex MediaRegex();

    [GeneratedRegex(@"title=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"class=""?ttl""?>([^<]*)<", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTitleRegex();

    public static IReadOnlyList<OnlineItem> Parse(string html, string quality)
    {
        // Skip <head> so meta tags are never read as cards.
        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (bodyStart > 0) html = html[bodyStart..];

        var seen = new HashSet<string>();
        var items = new List<OnlineItem>();

        foreach (Match m in MediaRegex().Matches(html))
        {
            var id = m.Groups[1].Value;
            var file = m.Groups[2].Value;
            if (!seen.Add(id)) continue;

            var anchorStart = html.LastIndexOf("<a ", m.Index, StringComparison.OrdinalIgnoreCase);
            if (anchorStart < 0 || m.Index - anchorStart > 600) { seen.Remove(id); continue; }

            var title = "";
            var anchorTag = html[anchorStart..Math.Min(html.Length, html.IndexOf('>', anchorStart) + 1)];
            var tm = TitleRegex().Match(anchorTag);
            if (tm.Success)
            {
                title = WebUtility.HtmlDecode(tm.Groups[1].Value).Trim();
                if (title.EndsWith(" live wallpaper", StringComparison.OrdinalIgnoreCase))
                    title = title[..^" live wallpaper".Length].Trim();
            }
            if (title.Length == 0)
            {
                var sm = SpanTitleRegex().Match(html, m.Index, Math.Min(400, html.Length - m.Index));
                if (sm.Success) title = WebUtility.HtmlDecode(sm.Groups[1].Value).Trim();
            }
            if (title.Length == 0) title = TitleFromSlug(file);
            if (title.Length == 0) title = $"MotionBGS {id}";

            var is4K = file.Contains("4k", StringComparison.OrdinalIgnoreCase) || file.Contains("3840x2160");
            var want4K = !string.Equals(quality, "hd", StringComparison.OrdinalIgnoreCase);
            var use4K = want4K && is4K;

            items.Add(new OnlineItem(
                Id: id,
                Title: title,
                Author: "MotionBGS",
                ThumbUrl: $"https://motionbgs.com/i/c/364x205/media/{id}/{file}.jpg",
                FullUrl: use4K ? $"https://motionbgs.com/dl/4k/{id}/" : $"https://motionbgs.com/dl/hd/{id}/",
                Resolution: use4K ? "4K" : "1080p",
                Source: OnlineSource.MotionBgs,
                IsVideo: true));
        }
        return items;
    }

    private static string TitleFromSlug(string file)
    {
        var slug = file.Split('.')[0];
        var words = slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.All(char.IsDigit))
            .Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w));
        return string.Join(' ', words);
    }
}
