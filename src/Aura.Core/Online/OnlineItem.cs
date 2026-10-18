namespace Aura.Core.Online;

public enum OnlineSource
{
    MotionBgs,
    Wallhaven,
    Bing,
    Minimalistic,
}

/// <summary>One wallpaper card returned by an online catalog.</summary>
public sealed record OnlineItem(
    string Id,
    string Title,
    string Author,
    string ThumbUrl,
    string FullUrl,
    string Resolution,
    OnlineSource Source,
    bool IsVideo)
{
    /// <summary>Stable key used for file names and caches.</summary>
    public string Key => $"{Source.ToString().ToLowerInvariant()}-{Id}";
}

/// <summary>The filters the Explore page sends to a catalog.</summary>
public sealed record OnlineQuery(
    OnlineSource Source,
    int Page = 1,
    string? Search = null,
    string Category = "all",
    string Sorting = "toplist",
    string Resolution = "all",
    string VideoQuality = "4k");
