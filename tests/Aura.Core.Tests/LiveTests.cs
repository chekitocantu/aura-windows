using Aura.Core.Online;

namespace Aura.Core.Tests;

/// <summary>Hits the real sites. Runs only with AURA_LIVE=1 so offline builds stay green.</summary>
public class LiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("AURA_LIVE") == "1";

    [Theory]
    [InlineData(OnlineSource.MotionBgs, 1, null, "all")]
    [InlineData(OnlineSource.MotionBgs, 2, null, "anime")]
    [InlineData(OnlineSource.MotionBgs, 1, "samurai", "all")]
    [InlineData(OnlineSource.Wallhaven, 1, null, "all")]
    [InlineData(OnlineSource.Bing, 1, null, "all")]
    [InlineData(OnlineSource.Bing, 2, null, "all")]
    [InlineData(OnlineSource.Minimalistic, 1, null, "all")]
    public async Task Source_returns_items(OnlineSource source, int page, string? search, string category)
    {
        if (!Enabled) return;
        var cache = Directory.CreateTempSubdirectory("aura-cache-").FullName;
        var items = await Catalog.FetchAsync(new OnlineQuery(source, page, search, category), cache, CancellationToken.None);
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.StartsWith("https://", i.ThumbUrl));
    }

    [Fact]
    public async Task MotionBgs_download_works()
    {
        if (!Enabled) return;
        var items = await MotionBgs.FetchAsync(new OnlineQuery(OnlineSource.MotionBgs, VideoQuality: "hd"), CancellationToken.None);
        var lib = Directory.CreateTempSubdirectory("aura-dl-").FullName;
        var file = await Downloader.DownloadAsync(items[0], lib, null, CancellationToken.None);
        Assert.Equal(".mp4", Path.GetExtension(file));
        Assert.True(new FileInfo(file).Length > 100_000);
        Assert.True(File.Exists(Aura.Core.Library.LibraryScanner.SidecarThumbPath(file)));
        Directory.Delete(lib, true);
    }
}
