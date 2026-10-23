using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trill
{
    public sealed class SpectrumView : FrameworkElement
    {
        private double[] levels = new double[48];
        private float[] wave = new float[256];
        private readonly Brush fill = new SolidColorBrush(Color.FromArgb(190, 184, 184, 184));
        private readonly Pen line = new Pen(new SolidColorBrush(Color.FromArgb(160, 233, 233, 233)), 1.3);
        public void Update(double[] spectrum, float[] waveform)
        {
            for (int i = 0; i < levels.Length; i++) levels[i] = Math.Max(spectrum[i], levels[i] * 0.76);
            wave = waveform;
            InvalidateVisual();
        }
        public void Clear() { Array.Clear(levels, 0, levels.Length); Array.Clear(wave, 0, wave.Length); InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            double step = ActualWidth / levels.Length;
            for (int i = 0; i < levels.Length; i++)
            {
                double height = Math.Max(2, levels[i] * ActualHeight * 0.7);
                drawing.DrawRoundedRectangle(fill, null, new Rect(i * step + 1, ActualHeight * 0.75 - height, Math.Max(1, step - 3), height), 2, 2);
            }
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(new Point(0, ActualHeight * 0.88), false, false);
                for (int i = 0; i < wave.Length; i++)
                    path.LineTo(new Point(i * ActualWidth / Math.Max(1, wave.Length - 1), ActualHeight * (0.88 - Math.Max(-1, Math.Min(1, wave[i])) * 0.1)), true, false);
            }
            geometry.Freeze();
            drawing.DrawGeometry(null, line, geometry);
        }
    }

    // Reads only the current local file. No microphone or system-audio capture.
    internal sealed class AudioSpectrum : IDisposable
    {
        private sealed class Block { public double Time; public float[] Values; }
        private readonly object gate = new object();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly Dispatcher dispatcher;
        private readonly Action<double[], float[]> deliver;
        private Thread worker;
        private string path;
        private double target;
        private int generation;
        private bool disposed;
        internal bool Unavailable { get; private set; }

        internal AudioSpectrum(Dispatcher ui, Action<double[], float[]> callback) { dispatcher = ui; deliver = callback; }
        internal void Open(string source)
        {
            lock (gate)
            {
                if (disposed) return;
                generation++; path = source; target = 0; Unavailable = false;
                if (worker == null) { worker = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Trill audio spectrum" }; worker.SetApartmentState(ApartmentState.MTA); worker.Start(); }
                ready.Set();
            }
        }
        internal void Request(double seconds) { lock (gate) { if (disposed || path == null) return; target = seconds; ready.Set(); } }
        internal void Cancel() { lock (gate) { if (disposed) return; generation++; path = null; ready.Set(); } }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; generation++; path = null; ready.Set(); } }

        private void Work()
        {
            Mf.SourceReader reader = null; bool started = false; int version = -1;
            var blocks = new List<Block>(); double end = -1; bool eos = false;
            try
            {
                while (true)
                {
                    ready.WaitOne();
                    string source; double wanted; int current;
                    lock (gate) { if (disposed) return; source = path; wanted = target; current = generation; }
                    if (current != version)
                    {
                        Mf.Release(reader); reader = null; blocks.Clear(); end = -1; eos = false;
                        if (started) { Mf.MFShutdown(); started = false; }
                        version = current;
                    }
                    if (source == null || Unavailable) continue;
                    try
                    {
                        if (reader == null)
                        {
                            if (!MediaFiles.IsLocalPath(source)) throw new NotSupportedException();
                            Mf.Check(Mf.MFStartup(0x20070, 1)); started = true;
                            Mf.Check(Mf.MFCreateSourceReaderFromURL(source, null, out reader));
                            Mf.Check(reader.SetStreamSelection(0xFFFFFFFE, false));
                            Mf.Check(reader.SetStreamSelection(0xFFFFFFFD, true));
                            Mf.MediaType format = null;
                            try
                            {
                                Mf.Check(Mf.MFCreateMediaType(out format));
                                Guid audio = new Guid("73647561-0000-0010-8000-00aa00389b71"), floating = new Guid("00000003-0000-0010-8000-00aa00389b71");
                                Guid channels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a"), rate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
                                Mf.Check(format.SetGUID(ref Mf.MajorType, ref audio)); Mf.Check(format.SetGUID(ref Mf.Subtype, ref floating));
                                Mf.Check(format.SetUINT32(ref channels, 1)); Mf.Check(format.SetUINT32(ref rate, 44100));
                                Mf.Check(reader.SetCurrentMediaType(0xFFFFFFFD, IntPtr.Zero, format));
                            }
                            finally { Mf.Release(format); }
                        }
                        if (end < 0 || wanted > end + 0.3 || (blocks.Count > 0 && wanted < blocks[0].Time))
                        {
                            Guid unit = Guid.Empty;
                            var position = new Mf.PositionValue { Type = 20, Value = (long)(Math.Max(0, wanted - 0.03) * 10000000) };
                            Mf.Check(reader.SetCurrentPosition(ref unit, ref position)); blocks.Clear(); end = -1; eos = false;
                        }
                        int guard = 0;
                        while (!eos && end < wanted + 2048.0 / 44100 && guard++ < 300)
                        {
                            lock (gate) { if (disposed || version != generation) break; }
                            Mf.Sample sample = null; Mf.Buffer buffer = null; bool locked = false;
                            try
                            {
                                uint stream, flags; long time;
                                Mf.Check(reader.ReadSample(0xFFFFFFFD, 0, out stream, out flags, out time, out sample));
                                eos = (flags & 2) != 0;
                                if (sample == null) continue;
                                Mf.Check(sample.ConvertToContiguousBuffer(out buffer));
                                IntPtr data; uint capacity, length;
                                Mf.Check(buffer.Lock(out data, out capacity, out length)); locked = true;
                                if (length > 4 * 1024 * 1024) throw new NotSupportedException();
                                float[] values = new float[length / 4];
                                Marshal.Copy(data, values, 0, values.Length);
                                var block = new Block { Time = time / 10000000.0, Values = values };
                                end = block.Time + values.Length / 44100.0;
                                if (end >= wanted - 0.1) blocks.Add(block);
                            }
                            finally { if (locked) buffer.Unlock(); Mf.Release(buffer); Mf.Release(sample); }
                        }
                        blocks.RemoveAll(b => b.Time + b.Values.Length / 44100.0 < wanted - 0.1);
                        var samples = new float[2048];
                        foreach (Block block in blocks)
                        {
                            int offset = (int)Math.Round((block.Time - wanted) * 44100);
                            int start = Math.Max(0, -offset), count = Math.Min(block.Values.Length - start, samples.Length - Math.Max(0, offset));
                            if (count > 0) Array.Copy(block.Values, start, samples, Math.Max(0, offset), count);
                        }
                        double[] levels = Analyze(samples);
                        var waveform = new float[256]; for (int i = 0; i < waveform.Length; i++) waveform[i] = samples[i * 8];
                        lock (gate) { if (disposed || version != generation || Math.Abs(target - wanted) > 0.15) continue; }
                        int deliveredVersion = version;
                        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                        {
                            lock (gate) { if (disposed || deliveredVersion != generation) return; }
                            deliver(levels, waveform);
                        }));
                    }
                    catch (Exception error)
                    {
                        if (!(error is COMException) && !(error is NotSupportedException) && !(error is System.IO.IOException) &&
                            !(error is UnauthorizedAccessException) && !(error is ArgumentException) && !(error is DllNotFoundException) &&
                            !(error is EntryPointNotFoundException) && !(error is InvalidOperationException)) throw;
                        lock (gate) { if (version == generation) Unavailable = true; }
                    }
                }
            }
            finally { Mf.Release(reader); if (started) Mf.MFShutdown(); ready.Dispose(); }
        }

        internal static double[] Analyze(float[] samples)
        {
            int n = samples.Length;
            var real = new double[n]; var imag = new double[n];
            for (int i = 0; i < n; i++) real[i] = samples[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)));
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { double value = real[i]; real[i] = real[j]; real[j] = value; }
            }
            for (int size = 2; size <= n; size <<= 1)
                for (int start = 0; start < n; start += size)
                    for (int k = 0; k < size / 2; k++)
                    {
                        double angle = -2 * Math.PI * k / size, c = Math.Cos(angle), s = Math.Sin(angle);
                        int a = start + k, b = a + size / 2;
                        double r = real[b] * c - imag[b] * s, v = real[b] * s + imag[b] * c;
                        real[b] = real[a] - r; imag[b] = imag[a] - v; real[a] += r; imag[a] += v;
                    }
            var result = new double[48];
            for (int band = 0; band < result.Length; band++)
            {
                int lo = Math.Max(1, (int)(40 * Math.Pow(400, band / 48.0) * n / 44100));
                int hi = Math.Min(n / 2, Math.Max(lo + 1, (int)(40 * Math.Pow(400, (band + 1) / 48.0) * n / 44100)));
                double peak = 0;
                for (int bin = lo; bin < hi; bin++) peak = Math.Max(peak, Math.Sqrt(real[bin] * real[bin] + imag[bin] * imag[bin]) * 4 / n);
                result[band] = Math.Max(0, Math.Min(1, (20 * Math.Log10(Math.Max(0.000001, peak)) + 65) / 65));
            }
            return result;
        }
    }
}

