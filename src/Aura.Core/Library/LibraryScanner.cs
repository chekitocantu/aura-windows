namespace Aura.Core.Library;

public sealed record LibraryItem(string Path, string Title, bool IsVideo, long Size, DateTime Modified)
{
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
}

/// <summary>Lists the wallpapers in the library folder (videos and images, not recursive into .thumbs).</summary>
public static class LibraryScanner
{
    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".gif"];
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    public const string ThumbFolder = ".thumbs";

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public static bool IsSupported(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext) || ImageExtensions.Contains(ext);
    }

    public static IReadOnlyList<LibraryItem> Scan(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var items = new List<LibraryItem>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MaxRecursionDepth = 3,
        };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            if (file.Contains(System.IO.Path.DirectorySeparatorChar + ThumbFolder + System.IO.Path.DirectorySeparatorChar))
                continue;
            if (!IsSupported(file)) continue;
            var info = new FileInfo(file);
            items.Add(new LibraryItem(file, System.IO.Path.GetFileNameWithoutExtension(file),
                IsVideo(file), info.Length, info.LastWriteTimeUtc));
        }
        return items.OrderByDescending(i => i.Modified).ToList();
    }

    /// <summary>Where the downloader stores the catalog thumbnail of a file.</summary>
    public static string SidecarThumbPath(string file) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, ThumbFolder,
            System.IO.Path.GetFileName(file) + ".jpg");
}
