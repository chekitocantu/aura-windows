namespace Aura.Core.Online;

/// <summary>Single entry point the UI calls, whatever the source.</summary>
public static class Catalog
{
    public static Task<IReadOnlyList<OnlineItem>> FetchAsync(OnlineQuery q, string cacheDir, CancellationToken ct) =>
        q.Source switch
        {
            OnlineSource.MotionBgs => MotionBgs.FetchAsync(q, ct),
            OnlineSource.Wallhaven => Wallhaven.FetchAsync(q, ct),
            OnlineSource.Bing => Bing.FetchAsync(q, cacheDir, ct),
            OnlineSource.Minimalistic => Minimalistic.FetchAsync(q, cacheDir, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(q)),
        };

    /// <summary>Whether the source supports a text search box.</summary>
    public static bool HasSearch(OnlineSource s) => s != OnlineSource.Bing;
}
