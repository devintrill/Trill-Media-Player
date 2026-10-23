using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace Trill
{
    // One decoder, one current request, and a bounded cache of neighboring frames.
    // The UI only reads frozen bitmaps; decoding, seeking, and disposal stay off it.
    internal sealed class ScrubPreview : IDisposable
    {
        internal const long CacheLimit = 128L * 1024 * 1024;
        private readonly object gate = new object();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly List<VideoFrame> frames = new List<VideoFrame>();
        private readonly Dispatcher dispatcher;
        private readonly Action frameReady;
        private bool deliveryQueued;
        private Thread worker;

        public ScrubPreview(Dispatcher uiDispatcher, Action onFrameReady, long memoryLimit = CacheLimit, int previewEdge = 1280)
        {
            cacheBudget = memoryLimit; edge = previewEdge;
            dispatcher = uiDispatcher;
            frameReady = onFrameReady;
        }
        private readonly long cacheBudget;
        private readonly int edge;
        private string path;
        private int generation;
        private double target;
        private int direction;
        private bool disposed, unavailable, ended, active;
        private long cacheBytes;
        private double frameDuration = 1.0 / 30;

        public bool Unavailable { get { lock (gate) return unavailable; } }
        public double FrameDuration { get { lock (gate) return frameDuration; } }

        public void Prepare(string videoPath)
        {
            Start(videoPath, 0);
            Stop();
        }

        public void Start(string videoPath, double seconds)
        {
            lock (gate)
            {
                if (disposed) return;
                if (!String.Equals(path, videoPath, StringComparison.OrdinalIgnoreCase))
                {
                    generation++; path = videoPath;
                    ended = false; frames.Clear(); cacheBytes = 0;
                }
                target = seconds; direction = 1; active = true; unavailable = false;
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.Normal, Name = "Trill frame previews" };
                    worker.SetApartmentState(ApartmentState.MTA);
                    worker.Start();
                }
                ready.Set();
            }
        }

        public void Request(double seconds)
        {
            lock (gate)
            {
                if (disposed || !active || path == null || Math.Abs(seconds - target) < 0.0000001) return;
                direction = seconds < target ? -1 : 1;
                target = seconds;
                ready.Set();
            }
        }

        public VideoFrame CurrentFrame()
        {
            lock (gate) return active ? Find(target) : null;
        }

        public void Stop()
        {
            lock (gate)
            {
                if (disposed) return;
                active = false;
                ready.Set();
            }
        }

        public void Cancel()
        {
            lock (gate)
            {
                if (disposed) return;
                generation++; path = null; active = false; frames.Clear(); cacheBytes = 0;
                ready.Set();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; generation++; path = null; active = false; frames.Clear(); cacheBytes = 0;
                ready.Set();
            }
        }

        private VideoFrame Find(double seconds)
        {
            VideoFrame first = null, last = null;
            foreach (VideoFrame frame in frames)
            {
                if (frame.Contains(seconds)) return frame;
                if (first == null || frame.Time < first.Time) first = frame;
                if (last == null || frame.Time > last.Time) last = frame;
            }
            if (first != null && seconds < first.Time && first.Time < 0.25) return first;
            return ended && last != null && seconds >= last.Time ? last : null;
        }

        private void Add(VideoFrame frame)
        {
            foreach (VideoFrame existing in frames)
                if (Math.Abs(existing.Time - frame.Time) < 0.000001) return;
            frames.Add(frame); cacheBytes += frame.Bytes;
            frameDuration = frame.Duration;
            if (frame.Contains(target) && !deliveryQueued && !dispatcher.HasShutdownStarted)
            {
                deliveryQueued = true;
                dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    lock (gate) { deliveryQueued = false; if (disposed) return; }
                    frameReady();
                }));
            }
            while (cacheBytes > cacheBudget || frames.Count > 180)
            {
                int farthest = 0;
                for (int i = 1; i < frames.Count; i++)
                    if (Math.Abs(frames[i].Time - target) > Math.Abs(frames[farthest].Time - target)) farthest = i;
                cacheBytes -= frames[farthest].Bytes;
                frames.RemoveAt(farthest);
            }
        }

        private void Work()
        {
            NativeVideoReader reader = null;
            int readerGeneration = -1;
            try
            {
                while (true)
                {
                    string source; int version, travel; double requested; bool wait, decoding;
                    lock (gate)
                    {
                        if (disposed) return;
                        source = path; version = generation; requested = target; travel = direction;
                        wait = unavailable; decoding = active;
                    }
                    if (readerGeneration != version)
                    {
                        if (reader != null) { reader.Dispose(); reader = null; }
                        readerGeneration = version;
                    }
                    if (source == null || wait) { ready.WaitOne(); continue; }
                    try
                    {
                        if (reader == null)
                        {
                            reader = new NativeVideoReader(source, edge);
                            lock (gate) { if (version == generation) frameDuration = reader.FrameDuration; }
                        }
                        if (!decoding) { ready.WaitOne(); continue; }
                        double from, until;
                        lock (gate)
                        {
                            if (version != generation) continue;
                            bool present = Find(target) != null;
                            // Scale the window to fit high-frame-rate and larger previews,
                            // so prefetching never chases a frame the cache must evict.
                            long bytes = frames.Count == 0 ? 1280L * 720 * 4 : frames[0].Bytes;
                            double capacitySeconds = Math.Max(2, Math.Min(176, cacheBudget / Math.Max(1, bytes) - 2)) * frameDuration;
                            // Reverse decodes forward from behind the pointer, so the wanted
                            // frame arrives last: widening this window costs latency directly.
                            double behind = Math.Min(0.8, capacitySeconds * 0.5);
                            double ahead = Math.Min(0.9, capacitySeconds * 0.3);
                            double neighbor = Math.Max(0, target + (travel < 0 ? -behind * 0.5 : ahead));
                            wait = present && Find(neighbor) != null;
                            from = Math.Max(0, requested - (travel < 0 ? behind : 0.08));
                            until = present ? (travel < 0 ? requested : neighbor) : requested;
                        }
                        if (wait) { ready.WaitOne(); continue; }
                        // Continue sequentially for adjacent forward frames. Reverse reads
                        // refill the area behind the playhead before the cache runs out.
                        if (reader.Position < 0 || reader.Position > until + 0.000001 || reader.Position < from - 0.75 || reader.Ended)
                            reader.Seek(from);
                        int empty = 0;
                        while (true)
                        {
                            double latest;
                            lock (gate)
                            {
                                if (disposed || !active || version != generation || Math.Abs(target - requested) > 0.3) break;
                                latest = target;
                            }
                            VideoFrame frame = reader.Read(Math.Max(0, latest - 0.85));
                            lock (gate)
                            {
                                if (disposed || version != generation) break;
                                ended = reader.Ended;
                                if (frame != null) Add(frame);
                            }
                            if (reader.Ended) break;
                            if (frame == null)
                            {
                                // Stream ticks are valid; a source with no progress is not.
                                if (++empty > 2000) throw new NotSupportedException("No video frames.");
                            }
                            else empty = 0;
                            if (reader.Position > until + 0.000001) break;
                        }
                    }
                    catch (Exception error)
                    {
                        if (!(error is System.Runtime.InteropServices.COMException) &&
                            !(error is System.IO.IOException) && !(error is UnauthorizedAccessException) &&
                            !(error is NotSupportedException) && !(error is ArgumentException) &&
                            !(error is System.Security.SecurityException) && !(error is DllNotFoundException) &&
                            !(error is EntryPointNotFoundException) && !(error is OverflowException))
                            throw;
                        if (reader != null) { reader.Dispose(); reader = null; }
                        lock (gate) { if (version == generation) unavailable = true; }
                    }
                }
            }
            finally { if (reader != null) reader.Dispose(); ready.Dispose(); }
        }
    }
}

