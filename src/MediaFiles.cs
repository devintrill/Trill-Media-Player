using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Trill
{
    public enum MediaKind { Photo, Video, Audio }

    public sealed class MediaItem
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public MediaKind MediaKind { get; private set; }
        public bool IsVideo { get { return MediaKind == MediaKind.Video; } }
        public bool IsAudio { get { return MediaKind == MediaKind.Audio; } }
        public bool IsPhoto { get { return MediaKind == MediaKind.Photo; } }

        // Video and audio both run through the media player and own a real
        // timeline; photos are held on screen by the slideshow clock instead.
        public bool HasTimeline { get { return MediaKind != MediaKind.Photo; } }

        public string Kind
        {
            get
            {
                switch (MediaKind)
                {
                    case MediaKind.Video: return "Video";
                    case MediaKind.Audio: return "Audio";
                    default: return "Photo";
                }
            }
        }

        public string Detail { get; private set; }

        private ThumbnailHandle thumb;

        // Created on first use so a playlist only decodes previews for rows that are shown.
        public ThumbnailHandle Thumb
        {
            get
            {
                if (thumb == null) thumb = new ThumbnailHandle(Path, MediaKind);
                return thumb;
            }
        }

        internal MediaItem(string path, MediaKind kind)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            MediaKind = kind;
            Detail = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + " / " + Kind;
        }
    }

    public static class MediaFiles
    {
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".mpg", ".mpeg", ".m2v", ".3gp", ".mts", ".m2ts"
        };

        private static readonly HashSet<string> PhotoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".wdp", ".jxr"
        };

        // Playback still depends on the decoders Windows has installed; a file that
        // cannot be decoded fails gracefully in the player rather than being hidden here.
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".m4a", ".aac", ".wav", ".wma", ".flac", ".ogg", ".oga", ".opus", ".aif", ".aiff", ".mka", ".ac3"
        };

        internal static string FileDialogPattern
        {
            get
            {
                var extensions = new List<string>();
                extensions.AddRange(PhotoExtensions);
                extensions.AddRange(VideoExtensions);
                extensions.AddRange(AudioExtensions);
                extensions.Sort(StringComparer.OrdinalIgnoreCase);
                return "*" + String.Join(";*", extensions.ToArray());
            }
        }

        public static bool IsSupported(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string extension = System.IO.Path.GetExtension(path);
                return VideoExtensions.Contains(extension) || PhotoExtensions.Contains(extension) || AudioExtensions.Contains(extension);
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryGetKind(string path, out MediaKind kind)
        {
            kind = MediaKind.Photo;
            if (String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string extension = System.IO.Path.GetExtension(path);
                if (VideoExtensions.Contains(extension)) { kind = MediaKind.Video; return true; }
                if (AudioExtensions.Contains(extension)) { kind = MediaKind.Audio; return true; }
                if (PhotoExtensions.Contains(extension)) { kind = MediaKind.Photo; return true; }
                return false;
            }
            catch (ArgumentException) { return false; }
        }

        // Checks existence as well as locality. Drive-letter paths are intentional:
        // URI, UNC, device, drive-relative, and rooted-relative paths are never opened.
        public static bool IsLocalPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length < 3 ||
                !IsDriveLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                return false;

            try
            {
                string fullPath = System.IO.Path.GetFullPath(path);
                string root = System.IO.Path.GetPathRoot(fullPath);
                DriveType type = new DriveInfo(root).DriveType;
                if (type != DriveType.Fixed && type != DriveType.Removable && type != DriveType.CDRom)
                    return false;

                // Inspect each ancestor before looking below it. A junction or symbolic
                // link can point to a network share despite having a local drive letter.
                string current = root;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                string[] segments = fullPath.Substring(root.Length).Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    current = System.IO.Path.Combine(current, segment);
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (SecurityException) { return false; }
        }

        // Skipped includes unsupported, inaccessible, missing, nonlocal, and duplicate entries.
        public static List<MediaItem> FromPaths(IEnumerable<string> paths, out int skipped)
        {
            skipped = 0;
            List<MediaItem> result = new List<MediaItem>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return result;

            foreach (string path in paths)
            {
                MediaKind kind;
                if (!TryGetKind(path, out kind) || !IsLocalPath(path))
                {
                    skipped++;
                    continue;
                }
                try
                {
                    string fullPath = System.IO.Path.GetFullPath(path);
                    if (!File.Exists(fullPath) || seen.Contains(fullPath))
                    {
                        skipped++;
                        continue;
                    }
                    using (FileStream readable = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        // Verify permission without reading or modifying media.
                    }
                    seen.Add(fullPath);
                    result.Add(new MediaItem(fullPath, kind));
                }
                catch (ArgumentException) { skipped++; }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
                catch (NotSupportedException) { skipped++; }
                catch (SecurityException) { skipped++; }
            }

            result.Sort(MediaItemComparer.Instance);
            return result;
        }

        public static List<MediaItem> FromFolder(string folder, out int skipped)
        {
            skipped = 0;
            if (!IsLocalPath(folder))
            {
                skipped = 1;
                return new List<MediaItem>();
            }
            try
            {
                // Top-level files only; subfolders are not visited automatically.
                return FromPaths(Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly), out skipped);
            }
            catch (ArgumentException) { skipped = 1; }
            catch (IOException) { skipped = 1; }
            catch (UnauthorizedAccessException) { skipped = 1; }
            catch (NotSupportedException) { skipped = 1; }
            catch (SecurityException) { skipped = 1; }
            return new List<MediaItem>();
        }

        private static bool IsDriveLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        internal sealed class MediaItemComparer : IComparer<MediaItem>
        {
            public static readonly MediaItemComparer Instance = new MediaItemComparer();

            public int Compare(MediaItem left, MediaItem right)
            {
                int result = CompareNatural(left.Name, right.Name);
                return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            }

            internal static int CompareNatural(string left, string right)
            {
                int a = 0;
                int b = 0;
                while (a < left.Length && b < right.Length)
                {
                    if (IsDigit(left[a]) && IsDigit(right[b]))
                    {
                        int aStart = a;
                        int bStart = b;
                        while (a < left.Length && IsDigit(left[a])) a++;
                        while (b < right.Length && IsDigit(right[b])) b++;
                        int aSignificant = aStart;
                        int bSignificant = bStart;
                        while (aSignificant < a && left[aSignificant] == '0') aSignificant++;
                        while (bSignificant < b && right[bSignificant] == '0') bSignificant++;

                        // Compare digits without parsing, so arbitrarily long numbers work.
                        int lengthResult = (a - aSignificant).CompareTo(b - bSignificant);
                        if (lengthResult != 0) return lengthResult;
                        int digitResult = String.Compare(left, aSignificant, right, bSignificant, a - aSignificant, StringComparison.Ordinal);
                        if (digitResult != 0) return digitResult;
                        int paddingResult = (a - aStart).CompareTo(b - bStart);
                        if (paddingResult != 0) return paddingResult;
                    }
                    else
                    {
                        int characterResult = Char.ToUpperInvariant(left[a]).CompareTo(Char.ToUpperInvariant(right[b]));
                        if (characterResult != 0) return characterResult;
                        a++;
                        b++;
                    }
                }
                return (left.Length - a).CompareTo(right.Length - b);
            }

            private static bool IsDigit(char value) { return value >= '0' && value <= '9'; }
        }
    }
}
