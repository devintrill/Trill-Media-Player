using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;

namespace Trill
{
    public sealed class RecentFolder
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public int ItemCount { get; private set; }
        public DateTime LastOpenedUtc { get; private set; }

        public RecentFolder(string path, int itemCount, DateTime lastOpenedUtc)
        {
            Path = path;
            string name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            Name = String.IsNullOrEmpty(name) ? path : name;
            ItemCount = itemCount;
            LastOpenedUtc = lastOpenedUtc;
        }

        public string Detail
        {
            get
            {
                string count = ItemCount == 1 ? "1 item" : ItemCount + " items";
                return count + "  ·  " + Ago(LastOpenedUtc);
            }
        }

        private static string Ago(DateTime utc)
        {
            TimeSpan elapsed = DateTime.UtcNow - utc;
            if (elapsed.TotalSeconds < 90) return "just now";
            if (elapsed.TotalMinutes < 60) return (int)elapsed.TotalMinutes + " min ago";
            if (elapsed.TotalHours < 24) return (int)elapsed.TotalHours + (elapsed.TotalHours < 2 ? " hour ago" : " hours ago");
            if (elapsed.TotalDays < 2) return "yesterday";
            if (elapsed.TotalDays < 30) return (int)elapsed.TotalDays + " days ago";
            return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
        }
    }

    // The one thing Trill keeps on disk: the folders you have played, so they can be
    // reopened as playlists later. Only folder paths, a count, and a timestamp are
    // written; individual file names are never recorded. Clear() deletes the file.
    public static class RecentStore
    {
        public const int Limit = 24;

        private static readonly object Gate = new object();

        public static string FolderPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Trill");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(FolderPath, "recent-folders.txt"); }
        }

        public static List<RecentFolder> Load()
        {
            List<RecentFolder> result = new List<RecentFolder>();
            lock (Gate)
            {
                string[] lines;
                try
                {
                    if (!File.Exists(FilePath)) return result;
                    lines = File.ReadAllLines(FilePath);
                }
                catch (IOException) { return result; }
                catch (UnauthorizedAccessException) { return result; }
                catch (NotSupportedException) { return result; }
                catch (ArgumentException) { return result; }
                catch (SecurityException) { return result; }

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3) continue;
                    long ticks;
                    int count;
                    if (!Int64.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)) continue;
                    if (!Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) continue;
                    if (ticks < 0 || ticks > DateTime.MaxValue.Ticks) continue;
                    string path = parts[2];
                    if (String.IsNullOrWhiteSpace(path) || !seen.Add(path)) continue;
                    result.Add(new RecentFolder(path, count, new DateTime(ticks, DateTimeKind.Utc)));
                    if (result.Count >= Limit) break;
                }
            }
            return result;
        }

        public static void Record(string folder, int itemCount)
        {
            if (String.IsNullOrWhiteSpace(folder) || !MediaFiles.IsLocalPath(folder)) return;
            string full;
            try { full = Path.GetFullPath(folder); }
            catch (ArgumentException) { return; }
            catch (NotSupportedException) { return; }
            catch (PathTooLongException) { return; }
            catch (SecurityException) { return; }

            List<RecentFolder> entries = Load();
            List<RecentFolder> updated = new List<RecentFolder>();
            updated.Add(new RecentFolder(full, itemCount, DateTime.UtcNow));
            foreach (RecentFolder entry in entries)
            {
                if (String.Equals(entry.Path, full, StringComparison.OrdinalIgnoreCase)) continue;
                updated.Add(entry);
                if (updated.Count >= Limit) break;
            }
            Write(updated);
        }

        public static void Remove(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) return;
            List<RecentFolder> entries = Load();
            List<RecentFolder> updated = new List<RecentFolder>();
            foreach (RecentFolder entry in entries)
                if (!String.Equals(entry.Path, folder, StringComparison.OrdinalIgnoreCase)) updated.Add(entry);
            if (updated.Count == entries.Count) return;
            Write(updated);
        }

        public static void Clear()
        {
            lock (Gate)
            {
                try { if (File.Exists(FilePath)) File.Delete(FilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
                catch (SecurityException) { }
            }
        }

        private static void Write(List<RecentFolder> entries)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);
                    List<string> lines = new List<string>();
                    foreach (RecentFolder entry in entries)
                    {
                        // A path containing a tab or newline would corrupt the file; skip it.
                        if (entry.Path.IndexOf('\t') >= 0 || entry.Path.IndexOf('\n') >= 0 || entry.Path.IndexOf('\r') >= 0) continue;
                        lines.Add(entry.LastOpenedUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" +
                            entry.ItemCount.ToString(CultureInfo.InvariantCulture) + "\t" + entry.Path);
                    }
                    File.WriteAllLines(FilePath, lines.ToArray());
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
                catch (SecurityException) { }
            }
        }
    }
}
