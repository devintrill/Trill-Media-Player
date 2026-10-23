using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Trill;

internal static class FramePreviewTests
{
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSinkWriterFromURL(string path, IntPtr stream, Mf.Attributes attributes, out Writer writer);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateSample(out Mf.Sample sample);
    [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMemoryBuffer(uint size, out Mf.Buffer buffer);
    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Writer
    {
        [PreserveSig] int AddStream(Mf.MediaType type, out uint stream);
        [PreserveSig] int SetInputMediaType(uint stream, Mf.MediaType type, Mf.Attributes attributes);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(uint stream, Mf.Sample sample);
        [PreserveSig] int SendStreamTick(uint stream, long time);
        [PreserveSig] int PlaceMarker(uint stream, IntPtr context);
        [PreserveSig] int NotifyEndOfSegment(uint stream);
        [PreserveSig] int Flush(uint stream);
        [PreserveSig] int FinalizeWriting();
    }

    private static Mf.MediaType VideoType(Guid subtype)
    {
        Mf.MediaType type;
        Mf.Check(Mf.MFCreateMediaType(out type));
        Mf.Check(type.SetGUID(ref Mf.MajorType, ref Mf.VideoType));
        Mf.Check(type.SetGUID(ref Mf.Subtype, ref subtype));
        Mf.Check(type.SetUINT64(ref Mf.FrameSize, (1920UL << 32) | 1080));
        Mf.Check(type.SetUINT64(ref Mf.FrameRate, (60UL << 32) | 1));
        Mf.Check(type.SetUINT64(ref Mf.PixelAspectRatio, (1UL << 32) | 1));
        Mf.Check(type.SetUINT32(ref Mf.InterlaceMode, 2));
        return type;
    }

    // Generated locally with the installed Windows H.264 encoder. No downloaded clips.
    private static void MakeMovie(string path)
    {
        Mf.Check(Mf.MFStartup(0x20070, 1));
        Writer writer = null;
        Mf.MediaType output = null, input = null;
        try
        {
            Mf.Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, null, out writer));
            output = VideoType(new Guid("34363248-0000-0010-8000-00aa00389b71"));
            Guid bitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
            Mf.Check(output.SetUINT32(ref bitrate, 8000000));
            uint stream;
            Mf.Check(writer.AddStream(output, out stream));
            input = VideoType(new Guid("3231564e-0000-0010-8000-00aa00389b71")); // NV12
            Mf.Check(writer.SetInputMediaType(stream, input, null));
            Mf.Check(writer.BeginWriting());
            var bytes = new byte[1920 * 1080 * 3 / 2];
            for (int i = 1920 * 1080; i < bytes.Length; i++) bytes[i] = 128;
            for (int frame = 0; frame < 180; frame++)
            {
                for (int i = 0; i < 1920 * 1080; i++) bytes[i] = (byte)(30 + frame);
                Mf.Sample sample = null; Mf.Buffer buffer = null; bool locked = false;
                try
                {
                    Mf.Check(MFCreateSample(out sample));
                    Mf.Check(MFCreateMemoryBuffer((uint)bytes.Length, out buffer));
                    IntPtr data; uint capacity, count;
                    Mf.Check(buffer.Lock(out data, out capacity, out count)); locked = true;
                    Marshal.Copy(bytes, 0, data, bytes.Length);
                    Mf.Check(buffer.Unlock()); locked = false;
                    Mf.Check(buffer.SetCurrentLength((uint)bytes.Length));
                    Mf.Check(sample.AddBuffer(buffer));
                    Mf.Check(sample.SetSampleTime(frame * 10000000L / 60));
                    Mf.Check(sample.SetSampleDuration((frame + 1) * 10000000L / 60 - frame * 10000000L / 60));
                    Mf.Check(writer.WriteSample(stream, sample));
                }
                finally { if (locked) buffer.Unlock(); Mf.Release(buffer); Mf.Release(sample); }
            }
            Mf.Check(writer.FinalizeWriting());
        }
        finally { Mf.Release(writer); Mf.Release(input); Mf.Release(output); Mf.MFShutdown(); }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        string path = Path.GetFullPath(args[0]);
        try
        {
            Console.WriteLine("Generating a local 1080p / 60 fps H.264 fixture...");
            MakeMovie(path);
            using (var preview = new ScrubPreview(Dispatcher.CurrentDispatcher, delegate { }))
            {
                preview.Start(path, 1.01);
                Stopwatch first = Stopwatch.StartNew();
                Wait(delegate { return preview.CurrentFrame() != null || preview.Unavailable; }, 6000);
                if (preview.Unavailable || preview.CurrentFrame() == null) throw new Exception("H.264 preview unavailable.");
                Console.WriteLine("MEASURE: first H.264 frame " + first.Elapsed.TotalMilliseconds.ToString("0.0") + " ms.");
                if (preview.CurrentFrame().Bitmap.PixelWidth != 1280) throw new Exception("High-resolution preview was not resized.");
                var delays = new List<double>();
                int immediate = 0;
                for (int direction = 0; direction < 2; direction++)
                    for (int i = 0; i < 90; i++)
                    {
                        int index = direction == 0 ? 60 + i : 149 - i;
                        double target = (index + 0.1) / 60.0;
                        Stopwatch clock = Stopwatch.StartNew();
                        preview.Request(target);
                        if (preview.CurrentFrame() != null) immediate++;
                        Wait(delegate { return preview.CurrentFrame() != null; }, 2000);
                        VideoFrame frame = preview.CurrentFrame();
                        if (frame == null || !frame.Contains(target)) throw new Exception("Wrong frame at " + index);
                        delays.Add(clock.Elapsed.TotalMilliseconds);
                        Pump(16);
                    }
                delays.Sort();
                Console.WriteLine("PASS: all 180 successive forward/reverse H.264 frames match their requested timestamps.");
                Console.WriteLine("MEASURE: " + immediate + "/180 cache hits; frame availability median " + delays[90].ToString("0.0") + " ms, p95 " + delays[171].ToString("0.0") + " ms, max " + delays[179].ToString("0.0") + " ms.");
                if (immediate < 90) throw new Exception("Adjacent-frame cache reuse is too low.");
                double repeatTarget = (60 + 0.1) / 60.0;
                preview.Stop();
                if (preview.CurrentFrame() != null) throw new Exception("Inactive preview returned a frame.");
                preview.Start(path, repeatTarget);
                if (preview.CurrentFrame() == null) throw new Exception("Repeated drag lost neighboring cached frames.");
                Console.WriteLine("PASS: repeated dragging reuses the warm decoder and cached frames.");
                preview.Cancel();
                if (preview.CurrentFrame() != null) throw new Exception("Canceled preview retained a frame.");
            }
            // The worker must release its native source on cancel, without a UI wait.
            bool released = false;
            Stopwatch release = Stopwatch.StartNew();
            while (!released && release.ElapsedMilliseconds < 3000)
            {
                try { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } released = true; }
                catch (IOException) { Pump(10); }
            }
            if (!released) throw new Exception("Native reader retained the file after cancellation.");
            Console.WriteLine("PASS: cancel releases the frame cache and local video file.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Wait(Func<bool> ready, int timeout)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!ready() && clock.ElapsedMilliseconds < timeout) Pump(5);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}

