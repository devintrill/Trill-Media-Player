using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Trill
{
    // Previews are produced inside this process and held in memory only. Photos are
    // decoded at reduced size on a worker thread; video frames are grabbed from a
    // hidden media element. The Windows shell thumbnail cache is deliberately not
    // used, because asking the shell for a thumbnail writes one to thumbcache.db.
    public static class Thumbnails
    {
        public const int TileWidth = 168;
        public const int TileHeight = 104;

        private const int CacheLimit = 600;
        internal static int Generation { get { return Volatile.Read(ref generation); } }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, BitmapSource> Cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<string> CacheOrder = new Queue<string>();
        private static readonly Queue<Job> PhotoQueue = new Queue<Job>();
        private static readonly AutoResetEvent PhotoSignal = new AutoResetEvent(false);
        private static Thread photoWorker;
        private static int generation;
        private static bool stopped;

        public static void Request(string path, MediaKind kind, Action<BitmapSource> callback)
        {
            if (callback == null || String.IsNullOrEmpty(path)) return;

            BitmapSource cached;
            lock (Gate)
            {
                if (stopped) return;
                if (Cache.TryGetValue(path, out cached))
                {
                    // A miss is stored as null so a failed file is not retried on every scroll.
                    if (cached != null) callback(cached);
                    return;
                }
            }

            // Audio has no frame to show; the caller falls back to its glyph tile.
            if (kind == MediaKind.Audio) return;

            Job request = new Job(path, kind, callback, Dispatcher.CurrentDispatcher, Volatile.Read(ref generation));
            if (kind == MediaKind.Photo)
            {
                lock (Gate)
                {
                    PhotoQueue.Enqueue(request);
                    StartWorker();
                }
                PhotoSignal.Set();
            }
            else
            {
                lock (Gate)
                {
                    PhotoQueue.Enqueue(request);
                    StartWorker();
                }
                PhotoSignal.Set();
            }
        }

        // Called when the picker leaves a folder so queued work for entries that are
        // no longer on screen is abandoned instead of competing with the new folder.
        public static void CancelPending()
        {
            Interlocked.Increment(ref generation);
            lock (Gate) { PhotoQueue.Clear(); }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                stopped = true;
                PhotoQueue.Clear();
                Cache.Clear();
                CacheOrder.Clear();
            }
            PhotoSignal.Set();
        }

        private static void StartWorker()
        {
            if (photoWorker != null) return;
            photoWorker = new Thread(PhotoLoop);
            photoWorker.IsBackground = true;
            photoWorker.Priority = ThreadPriority.BelowNormal;
            photoWorker.Name = "Trill thumbnails";
            // Media Foundation needs a multithreaded apartment.
            photoWorker.SetApartmentState(ApartmentState.MTA);
            photoWorker.Start();
        }

        // Decoded with the same Media Foundation reader the scrubber uses, rather than
        // a hidden media element: no UI thread, no timeout, and no black-frame guesswork.
        private static BitmapSource DecodeVideoFrame(string path)
        {
            NativeVideoReader reader = null;
            try
            {
                reader = new NativeVideoReader(path, 320);
                // A little way in, to skip the black lead-in many files open with.
                BitmapSource picture = FirstFrame(reader, 1.0);
                return picture ?? FirstFrame(reader, 0);
            }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (OverflowException) { return null; }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            catch (SecurityException) { return null; }
            finally { if (reader != null) reader.Dispose(); }
        }

        private static BitmapSource FirstFrame(NativeVideoReader reader, double seconds)
        {
            reader.Seek(seconds);
            for (int attempt = 0; attempt < 600 && !reader.Ended; attempt++)
            {
                VideoFrame frame = reader.Read(0);
                if (frame != null) return frame.Bitmap;
            }
            return null;
        }

        private static void Store(string path, BitmapSource image)
        {
            lock (Gate)
            {
                if (stopped || Cache.ContainsKey(path)) return;
                Cache[path] = image;
                CacheOrder.Enqueue(path);
                while (CacheOrder.Count > CacheLimit) Cache.Remove(CacheOrder.Dequeue());
            }
        }

        private static bool IsCurrent(Job request)
        {
            return request.Generation == Volatile.Read(ref generation);
        }

        private static void Complete(Job request, BitmapSource image)
        {
            if (!IsCurrent(request)) return;
            Store(request.Path, image);
            if (image == null) return;
            try
            {
                request.Dispatcher.BeginInvoke(DispatcherPriority.Background, request.Callback, image);
            }
            catch (InvalidOperationException) { }
        }

        private static void PhotoLoop()
        {
            while (true)
            {
                Job request = null;
                lock (Gate)
                {
                    if (stopped) return;
                    if (PhotoQueue.Count > 0) request = PhotoQueue.Dequeue();
                }
                if (request == null) { PhotoSignal.WaitOne(); continue; }
                if (!IsCurrent(request)) continue;
                Complete(request, request.Kind == MediaKind.Photo
                    ? DecodePhoto(request.Path)
                    : DecodeVideoFrame(request.Path));
            }
        }

        private static BitmapSource DecodePhoto(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    BitmapDecoder probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    if (probe.Frames.Count == 0) return null;
                    int width = probe.Frames[0].PixelWidth;
                    int height = probe.Frames[0].PixelHeight;
                    if (width <= 0 || height <= 0) return null;

                    BitmapImage bitmap = new BitmapImage();
                    stream.Position = 0;
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.None;
                    // Decode straight to tile size; the full image is never held for a preview.
                    if (width * TileHeight >= height * TileWidth) bitmap.DecodePixelWidth = Math.Min(width, TileWidth);
                    else bitmap.DecodePixelHeight = Math.Min(height, TileHeight);
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (FileFormatException) { return null; }
            catch (ArgumentException) { return null; }
            catch (SecurityException) { return null; }
            catch (OverflowException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
        }

        private sealed class Job
        {
            public readonly string Path;
            public readonly MediaKind Kind;
            public readonly Delegate Callback;
            public readonly Dispatcher Dispatcher;
            public readonly int Generation;

            public Job(string path, MediaKind kind, Action<BitmapSource> callback, Dispatcher dispatcher, int generation)
            {
                Path = path;
                Kind = kind;
                Callback = callback;
                Dispatcher = dispatcher;
                Generation = generation;
            }
        }

    }

    // Binds one preview to one row. The image is requested the first time a binding
    // reads it, so a virtualised list only fetches the tiles actually on screen.
    public sealed class ThumbnailHandle : INotifyPropertyChanged
    {
        private readonly string path;
        private readonly MediaKind kind;
        private WeakReference image;
        private bool started;
        private int requestGeneration = -1;
        private BitmapSource CurrentImage { get { return image == null ? null : image.Target as BitmapSource; } }

        public ThumbnailHandle(string path, MediaKind kind)
        {
            this.path = path;
            this.kind = kind;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public BitmapSource Image
        {
            get
            {
                Begin();
                return CurrentImage;
            }
        }

        public bool HasImage { get { return CurrentImage != null; } }

        public void Begin()
        {
            if (CurrentImage != null) return;
            if (started && requestGeneration == Thumbnails.Generation && image == null) return;
            started = true;
            requestGeneration = Thumbnails.Generation;
            image = null;
            Thumbnails.Request(path, kind, delegate(BitmapSource result)
            {
                image = new WeakReference(result);
                Raise("Image");
                Raise("HasImage");
            });
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
