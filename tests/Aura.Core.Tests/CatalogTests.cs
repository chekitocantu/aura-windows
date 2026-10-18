using Aura.Core;
using Aura.Core.Library;
using Aura.Core.Online;

namespace Aura.Core.Tests;

public class CatalogTests
{
    private const string MotionHtml = """
        <html><head><meta content="/media/1/meta.jpg"></head><body>
        <div class=tmb>
          <a title="Celestial Battle Gojo vs Mahoraga live wallpaper" href=/celestial-battle-gojo-vs-mahoraga>
            <figure><picture><img src=/i/c/364x205/media/9967/celestial-battle-gojo-vs-mahoraga.3840x2160.jpg width=364></picture></figure>
            <span class=ttl>Celestial Battle Gojo vs Mahoraga</span>
          </a>
          <a href=/rainy-night>
            <img src=/i/c/364x205/media/10102/rainy-night-city.1920x1080.jpg>
            <span class=ttl>Rainy &amp; Night</span>
          </a>
          <a href=/again><img src=/i/c/364x205/media/9967/celestial-battle-gojo-vs-mahoraga.3840x2160.jpg></a>
        </div></body></html>
        """;

    [Fact]
    public void MotionBgs_parses_cards_and_skips_duplicates_and_head()
    {
        var items = MotionBgs.Parse(MotionHtml, "4k");
        Assert.Equal(2, items.Count);

        Assert.Equal("9967", items[0].Id);
        Assert.Equal("Celestial Battle Gojo vs Mahoraga", items[0].Title);
        Assert.Equal("https://motionbgs.com/dl/4k/9967/", items[0].FullUrl);
        Assert.Equal("4K", items[0].Resolution);
        Assert.True(items[0].IsVideo);
        Assert.Equal("https://motionbgs.com/i/c/364x205/media/9967/celestial-battle-gojo-vs-mahoraga.3840x2160.jpg",
            items[0].ThumbUrl);

        Assert.Equal("Rainy & Night", items[1].Title);
        Assert.Equal("https://motionbgs.com/dl/hd/10102/", items[1].FullUrl);
    }

    [Fact]
    public void MotionBgs_hd_preference_uses_hd_links()
    {
        var items = MotionBgs.Parse(MotionHtml, "hd");
        Assert.All(items, i => Assert.Contains("/dl/hd/", i.FullUrl));
    }

    [Theory]
    [InlineData(1, null, "all", "https://motionbgs.com/")]
    [InlineData(2, null, "all", "https://motionbgs.com/2/")]
    [InlineData(1, null, "anime", "https://motionbgs.com/tag:anime/")]
    [InlineData(3, null, "anime", "https://motionbgs.com/tag:anime/3/")]
    [InlineData(1, "gojo satoru", "anime", "https://motionbgs.com/search?q=gojo+satoru")]
    [InlineData(2, "gojo", "all", "https://motionbgs.com/search?q=gojo&page=2")]
    public void MotionBgs_urls(int page, string? search, string cat, string expected) =>
        Assert.Equal(expected, MotionBgs.BuildUrl(page, search, cat));

    [Fact]
    public void Wallhaven_parses_api_json()
    {
        const string json = """
            {"data":[{"id":"x9k123","resolution":"3840x2160","path":"https://w.wallhaven.cc/full/x9/wallhaven-x9k123.jpg",
              "category":"anime","thumbs":{"large":"https://th.wallhaven.cc/lg/x9/x9k123.jpg","small":"s"}},
              {"id":"nopath"}]}
            """;
        var items = Wallhaven.Parse(json);
        var item = Assert.Single(items);
        Assert.Equal("x9k123", item.Id);
        Assert.Equal("https://th.wallhaven.cc/lg/x9/x9k123.jpg", item.ThumbUrl);
        Assert.False(item.IsVideo);
    }

    [Fact]
    public void Wallhaven_url_has_filters()
    {
        var url = Wallhaven.BuildUrl(new OnlineQuery(OnlineSource.Wallhaven, 2, "city night", "anime", "toplist", "4k"));
        Assert.Contains("categories=010", url);
        Assert.Contains("purity=100", url);
        Assert.Contains("atleast=3840x2160", url);
        Assert.Contains("topRange=1M", url);
        Assert.Contains("q=city+night", url);
        Assert.Contains("page=2", url);
    }

    [Fact]
    public void Bing_parses_daily_and_archive()
    {
        var daily = Bing.ParseDaily("""
            {"images":[{"urlbase":"/th?id=OHR.MountFuji_EN-US123","copyright":"Fuji (© X)","title":"","startdate":"20261018","hsh":"abc"}]}
            """);
        var d = Assert.Single(daily);
        Assert.Equal("Mount Fuji", d.Title);
        Assert.Equal("https://www.bing.com/th?id=OHR.MountFuji_EN-US123_UHD.jpg", d.FullUrl);
        Assert.Contains("2026-10-18", d.Resolution);

        var archive = Bing.ParseArchive("""{"Total":1,"data":[{"urlbase":"/th?id=OHR.A_EN-US","title":"Wave","hsh":"z"}]}""");
        Assert.Equal("Wave", Assert.Single(archive).Title);
    }

    [Fact]
    public void Minimalistic_parses_only_images()
    {
        var items = Minimalistic.Parse("""
            [{"name":"alena-aenami_clouds.jpg","download_url":"https://raw.githubusercontent.com/x/images/alena-aenami_clouds.jpg","size":2097152},
             {"name":"README.md","download_url":"https://x/README.md"}]
            """);
        var item = Assert.Single(items);
        Assert.Equal("Alena Aenami Clouds", item.Title);
        Assert.StartsWith("https://wsrv.nl/?url=https%3A%2F%2Fraw.githubusercontent.com", item.ThumbUrl);
    }

    [Theory]
    [InlineData("Gojo: vs/Mahoraga?", "Gojo vs Mahoraga")]
    [InlineData("   ", "wallpaper")]
    public void Safe_file_names(string title, string expected) =>
        Assert.Equal(expected, Downloader.SafeName(title));

    [Fact]
    public void Library_scan_finds_media_and_ignores_thumbs()
    {
        var dir = Directory.CreateTempSubdirectory("aura-lib-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.mp4"), "x");
            File.WriteAllText(Path.Combine(dir, "b.png"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(dir, LibraryScanner.ThumbFolder));
            File.WriteAllText(Path.Combine(dir, LibraryScanner.ThumbFolder, "a.mp4.jpg"), "x");

            var items = LibraryScanner.Scan(dir);
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.IsVideo && i.Title == "a");
            Assert.Contains(items, i => !i.IsVideo && i.Title == "b");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Settings_roundtrip_and_repair()
    {
        var file = Path.Combine(Path.GetTempPath(), $"aura-{Guid.NewGuid():N}.json");
        try
        {
            var s = AuraSettings.Load(file, "/lib");
            Assert.Equal("/lib", s.LibraryFolder);
            s.Wallpapers["\\\\.\\DISPLAY1"] = "/lib/a.mp4";
            s.Volume = 300;
            s.Save(file);

            var again = AuraSettings.Load(file, "/other");
            Assert.Equal("/lib", again.LibraryFolder);
            Assert.Equal(100, again.Volume);
            Assert.Equal("/lib/a.mp4", again.Wallpapers["\\\\.\\DISPLAY1"]);

            File.WriteAllText(file, "{ broken");
            Assert.Equal("fill", AuraSettings.Load(file, "/x").Fit);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Strings_have_both_languages()
    {
        Strings.Init("en");
        Assert.Equal("Library", Strings.T("nav.library"));
        Strings.Init("es");
        Assert.Equal("Biblioteca", Strings.T("nav.library"));
        Assert.Equal("missing.key", Strings.T("missing.key"));
    }
}
