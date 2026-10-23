using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Trill
{
    // A single background decoder keeps only the newest request. Rapid Next/Back
    // input cannot build a queue of full-size photos or show an obsolete result.
    internal sealed class PhotoLoader : IDisposable
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly Dispatcher dispatcher;
        private Thread worker;
        private Action<BitmapSource> completion;
        private string pendingPath;
        private int version;
        private bool disposed;

        public PhotoLoader(Dispatcher dispatcher) { this.dispatcher = dispatcher; }

        public void Load(string path, Action<BitmapSource> completed)
        {
            lock (gate)
            {
                if (disposed) return;
                version++;
                pendingPath = path;
                completion = completed;
                if (worker == null)
                {
                    worker = new Thread(DecodeLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Trill photo loader" };
                    worker.Start();
                }
            }
            ready.Set();
        }

        public void Cancel()
        {
            lock (gate) { version++; pendingPath = null; completion = null; }
        }

        public void Dispose()
        {
            lock (gate) { disposed = true; version++; pendingPath = null; completion = null; }
            ready.Set();
        }

        private void DecodeLoop()
        {
            while (true)
            {
                string path;
                int requestVersion;
                Action<BitmapSource> callback;
                lock (gate)
                {
                    if (disposed) return;
                    path = pendingPath; pendingPath = null;
                    callback = completion; completion = null;
                    requestVersion = version;
                }
                if (path == null) { ready.WaitOne(); continue; }
                BitmapSource bitmap = Decode(path);
                lock (gate) { if (disposed || requestVersion != version) continue; }
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        lock (gate) { if (disposed || requestVersion != version) return; }
                        callback(bitmap);
                    }));
                }
                catch (InvalidOperationException) { }
            }
        }

        private static BitmapSource Decode(string path)
        {
            try
            {
                if (!MediaFiles.IsLocalPath(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int width = decoder.Frames[0].PixelWidth;
                    int height = decoder.Frames[0].PixelHeight;
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (Math.Max(width, height) > 3840)
                    {
                        if (width >= height) bitmap.DecodePixelWidth = 3840;
                        else bitmap.DecodePixelHeight = 3840;
                    }
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (System.IO.FileFormatException) { return null; }
            catch (ArgumentException) { return null; }
            catch (SecurityException) { return null; }
            catch (OverflowException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
        }
    }
}
