using System;
using System.Collections.Generic;
using System.IO;
using Trill;

internal static class MediaFilesTests
{
    private static int assertions;

    private static int Main(string[] args)
    {
        string testRoot = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "Trill-MediaFiles-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(testRoot);
        try
        {
            TestExtensions();
            TestLocalPaths(testRoot);
            TestReparsePaths(testRoot);
            TestLoading(testRoot);
            TestNaturalOrder(testRoot);
            TestFolders(testRoot);
            Console.WriteLine("PASS: " + assertions + " media-file assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }

    }

    private static void TestExtensions()
    {
        string[] videos = { "mp4", "m4v", "mov", "wmv", "avi", "mkv", "webm", "mpg", "mpeg", "m2v", "3gp", "mts", "m2ts" };
        string[] photos = { "jpg", "jpeg", "png", "bmp", "gif", "tif", "tiff", "wdp", "jxr" };
        foreach (string extension in videos) Assert(MediaFiles.IsSupported("file." + extension), extension + " should be supported");
        foreach (string extension in photos) Assert(MediaFiles.IsSupported("file." + extension.ToUpperInvariant()), extension + " should be case insensitive");
        Assert(!MediaFiles.IsSupported("file.mp4.exe"), "A double extension must not bypass filtering");
        Assert(!MediaFiles.IsSupported("file.txt"), "Text should be unsupported");
        Assert(!MediaFiles.IsSupported(null), "Null should be unsupported");
        Assert(!MediaFiles.IsSupported("bad\0.jpg"), "Invalid path should be unsupported");
    }

    private static void TestLocalPaths(string root)
    {
        string photo = Write(root, "local.jpg");
        Assert(MediaFiles.IsLocalPath(photo), "An existing file on the local test drive should be local");
        Assert(MediaFiles.IsLocalPath(root), "An existing local directory should be local");
        Assert(MediaFiles.IsLocalPath(photo.Replace('\\', '/')), "Forward slash local paths should work");
        Assert(!MediaFiles.IsLocalPath(Path.Combine(root, "missing.jpg")), "Missing files should be rejected");
        Assert(!MediaFiles.IsLocalPath("local.jpg"), "Relative paths should be rejected");
        Assert(!MediaFiles.IsLocalPath("C:local.jpg"), "Drive-relative paths should be rejected");
        Assert(!MediaFiles.IsLocalPath("\\local.jpg"), "Rooted-relative paths should be rejected");
        Assert(!MediaFiles.IsLocalPath("https://example.com/movie.mp4"), "HTTPS addresses should be rejected");
        Assert(!MediaFiles.IsLocalPath("http://localhost/movie.mp4"), "Localhost URLs should be rejected");
        Assert(!MediaFiles.IsLocalPath(new Uri(photo).AbsoluteUri), "File URI syntax should be rejected");
        Assert(!MediaFiles.IsLocalPath("\\\\server\\share\\photo.jpg"), "UNC paths should be rejected before any access");
        Assert(!MediaFiles.IsLocalPath("//server/share/photo.jpg"), "Slash UNC paths should be rejected");
        Assert(!MediaFiles.IsLocalPath("\\\\?\\C:\\photo.jpg"), "Device paths should be rejected");
        Assert(!MediaFiles.IsLocalPath(null), "Null path should be rejected");
        Assert(!MediaFiles.IsLocalPath(" "), "Blank path should be rejected");
        Assert(!MediaFiles.IsLocalPath(root + "\\bad\0.jpg"), "Invalid path should be rejected without throwing");
    }

    private static void TestReparsePaths(string root)
    {
        string linkedFolder = Path.Combine(root, "linked-folder");
        string linkedFile = Path.Combine(linkedFolder, "linked.jpg");
        Assert(Directory.Exists(linkedFolder), "Runner must create a junction fixture");
        Assert(!MediaFiles.IsLocalPath(linkedFolder), "Junction folders must be rejected");
        Assert(!MediaFiles.IsLocalPath(linkedFile), "Files beneath junctions must be rejected");
        int skipped;
        Assert(MediaFiles.FromPaths(new string[] { linkedFile }, out skipped).Count == 0 && skipped == 1,
            "Media loading must reject files reached through a junction");
        Assert(MediaFiles.FromFolder(linkedFolder, out skipped).Count == 0 && skipped == 1,
            "Folder scanning must reject a junction");
    }

    private static void TestLoading(string root)
    {
        string folder = Path.Combine(root, "loading");
        Directory.CreateDirectory(folder);
        string video = Write(folder, "Clip10.MP4");
        string photo = Write(folder, "clip2.png");
        string text = Write(folder, "notes.txt");
        string imageDirectory = Path.Combine(folder, "folder.jpg");
        Directory.CreateDirectory(imageDirectory);
        int skipped;
        List<MediaItem> items = MediaFiles.FromPaths(new string[] {
            video, photo, video.ToLowerInvariant(), text, Path.Combine(folder, "missing.jpg"),
            "https://example.com/remote.mp4", "\\\\server\\share\\remote.jpg", null, imageDirectory
        }, out skipped);
        Assert(items.Count == 2, "Only unique supported existing local files should load");
        Assert(skipped == 7, "Every rejected entry should contribute to skipped count");
        Assert(items[0].Name == "clip2.png" && items[1].Name == "Clip10.MP4", "Names should sort naturally ignoring case");
        Assert(!items[0].IsVideo && items[0].Kind == "Photo", "Image metadata should identify photos");
        Assert(items[1].IsVideo && items[1].Kind == "Video", "Video metadata should identify videos");
        Assert(items[1].Detail.Contains("MP4"), "Detail should include extension");
        Assert(items[0].Path == Path.GetFullPath(photo), "Item path should be absolute");
        Assert(MediaFiles.FromPaths(null, out skipped).Count == 0 && skipped == 0, "Null input should be an empty result");
        Assert(MediaFiles.FromPaths(new string[0], out skipped).Count == 0 && skipped == 0, "Empty input should be an empty result");
        using (FileStream locked = new FileStream(photo, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert(MediaFiles.FromPaths(new string[] { photo, video }, out skipped).Count == 1 && skipped == 1,
                "Unreadable locked files should be skipped while readable files continue loading");
        }
    }

    private static void TestNaturalOrder(string root)
    {
        string folder = Path.Combine(root, "ordering");
        Directory.CreateDirectory(folder);
        string[] expected = {
            "image0.jpg", "image00.jpg", "Image2.jpg", "image02.jpg", "image9.jpg", "image10.jpg",
            "image9999999999999999999999999999.jpg", "image10000000000000000000000000000.jpg"
        };
        List<string> paths = new List<string>();
        for (int i = expected.Length - 1; i >= 0; i--) paths.Add(Write(folder, expected[i]));
        int skipped;
        List<MediaItem> items = MediaFiles.FromPaths(paths, out skipped);
        Assert(skipped == 0 && items.Count == expected.Length, "All numbered images should load");
        for (int i = 0; i < expected.Length; i++) Assert(items[i].Name == expected[i], "Natural order mismatch at " + i);
        string equivalent = Path.Combine(folder, ".", expected[0]);
        items = MediaFiles.FromPaths(new string[] { paths[paths.Count - 1], equivalent }, out skipped);
        Assert(items.Count == 1 && skipped == 1, "Normalized equivalent paths should be deduplicated");
    }

    private static void TestFolders(string root)
    {
        string folder = Path.Combine(root, "folders");
        Directory.CreateDirectory(folder);
        Write(folder, "image1.jpg");
        string file = Write(folder, "video2.mp4");
        Write(folder, "ignore.txt");
        string nested = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nested);
        Write(nested, "hidden.jpg");
        int skipped;
        List<MediaItem> items = MediaFiles.FromFolder(folder, out skipped);
        Assert(items.Count == 2 && skipped == 1, "Folder scanning should filter and stay in the selected folder");
        Assert(MediaFiles.FromFolder(Path.Combine(root, "missing-folder"), out skipped).Count == 0 && skipped == 1, "Missing folder should fail gracefully");
        Assert(MediaFiles.FromFolder(file, out skipped).Count == 0 && skipped == 1, "File supplied as folder should fail gracefully");
        Assert(MediaFiles.FromFolder("\\\\server\\share", out skipped).Count == 0 && skipped == 1, "Network folder should fail gracefully");
        Assert(MediaFiles.FromFolder(null, out skipped).Count == 0 && skipped == 1, "Null folder should fail gracefully");
    }

    private static string Write(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, "Local media-file loader test fixture; no decoder involved.");
        return path;
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
