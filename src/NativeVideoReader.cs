using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Trill
{
    internal sealed class VideoFrame
    {
        public BitmapSource Bitmap;
        public double Time;
        public double Duration;
        public long Bytes { get { return Bitmap == null ? 0 : (long)Bitmap.PixelWidth * Bitmap.PixelHeight * 4; } }
        public bool Contains(double seconds) { return seconds + 0.000001 >= Time && seconds < Time + Duration - 0.000001; }
    }

    // A private, synchronous source reader used only on the preview worker. It never
    // drives the playback clock and never opens audio, URLs, or a network source.
    internal sealed class NativeVideoReader : IDisposable
    {
        private Mf.SourceReader reader;
        private bool started;
        private int width, height, stride, rotation;
        private readonly int previewEdge;
        private byte[] pixels;
        private double frameDuration = 1.0 / 30;
        public double FrameDuration { get { return frameDuration; } }
        public double Position { get; private set; }
        public bool Ended { get; private set; }

        public NativeVideoReader(string path, int previewEdge = 1280)
        {
            if (!MediaFiles.IsLocalPath(path)) throw new ArgumentException("A local video is required.");
            this.previewEdge = previewEdge;
            Mf.Attributes attributes = null;
            Mf.MediaType native = null, output = null;
            try
            {
                Mf.Check(Mf.MFStartup(0x20070, 1)); // MFSTARTUP_NOSOCKET
                started = true;
                Mf.Check(Mf.MFCreateAttributes(out attributes, 2));
                Mf.Check(attributes.SetUINT32(ref Mf.AdvancedProcessing, 1));
                Mf.Check(Mf.MFCreateSourceReaderFromURL(path, attributes, out reader));
                Mf.Check(reader.SetStreamSelection(0xFFFFFFFE, false));
                Mf.Check(reader.SetStreamSelection(Mf.VideoStream, true));
                Mf.Check(reader.GetNativeMediaType(Mf.VideoStream, 0, out native));
                ulong size, aspect, rate;
                Mf.Check(native.GetUINT64(ref Mf.FrameSize, out size));
                double w = (uint)(size >> 32), h = (uint)size;
                if (w < 1 || h < 1 || w > 16384 || h > 16384) throw new NotSupportedException("Invalid video size.");
                if (native.GetUINT64(ref Mf.PixelAspectRatio, out aspect) >= 0 && (uint)aspect != 0)
                    w *= (double)(uint)(aspect >> 32) / (uint)aspect;
                uint angle;
                if (native.GetUINT32(ref Mf.VideoRotation, out angle) >= 0) rotation = (int)angle;
                if (native.GetUINT64(ref Mf.FrameRate, out rate) >= 0 && (uint)(rate >> 32) > 0 && (uint)rate > 0)
                    frameDuration = (double)(uint)rate / (uint)(rate >> 32);
                double scale = Math.Min(1, (double)previewEdge / Math.Max(w, h));
                width = Math.Max(2, (int)Math.Round(w * scale) / 2 * 2);
                height = Math.Max(2, (int)Math.Round(h * scale) / 2 * 2);
                Mf.Check(Mf.MFCreateMediaType(out output));
                Mf.Check(output.SetGUID(ref Mf.MajorType, ref Mf.VideoType));
                Mf.Check(output.SetGUID(ref Mf.Subtype, ref Mf.Rgb32));
                Mf.Check(output.SetUINT64(ref Mf.FrameSize, ((ulong)width << 32) | (uint)height));
                Mf.Check(output.SetUINT64(ref Mf.PixelAspectRatio, (1UL << 32) | 1));
                Mf.Check(output.SetUINT32(ref Mf.InterlaceMode, 2)); // progressive preview
                Mf.Check(reader.SetCurrentMediaType(Mf.VideoStream, IntPtr.Zero, output));
                ReadFormat();
                Position = -1;
            }
            catch
            {
                Mf.Release(output); output = null;
                Mf.Release(native); native = null;
                Mf.Release(attributes); attributes = null;
                Dispose();
                throw;
            }
            finally { Mf.Release(output); Mf.Release(native); Mf.Release(attributes); }
        }

        private void ReadFormat()
        {
            Mf.MediaType current = null;
            try
            {
                Mf.Check(reader.GetCurrentMediaType(Mf.VideoStream, out current));
                ulong size;
                Mf.Check(current.GetUINT64(ref Mf.FrameSize, out size));
                width = (int)(size >> 32); height = (int)(uint)size;
                // Bound preview allocations even if a converter ignores the request.
                int bound = Math.Max(16, previewEdge) + 16;
                if (width < 1 || height < 1 || width > bound || height > bound)
                    throw new NotSupportedException("The decoder cannot resize the preview.");
                uint value;
                stride = current.GetUINT32(ref Mf.DefaultStride, out value) >= 0 ? unchecked((int)value) : width * 4;
                if (Math.Abs((long)stride) < width * 4L || Math.Abs((long)stride) * height > 64 * 1024 * 1024)
                    throw new NotSupportedException("Invalid video stride.");
                pixels = new byte[checked(width * height * 4)];
            }
            finally { Mf.Release(current); }
        }

        public void Seek(double seconds)
        {
            var position = new Mf.PositionValue { Type = 20, Value = (long)(Math.Max(0, seconds) * 10000000) };
            Guid format = Guid.Empty;
            Mf.Check(reader.SetCurrentPosition(ref format, ref position));
            Position = -1; Ended = false;
        }

        public VideoFrame Read(double keepFrom)
        {
            Mf.Sample sample = null;
            Mf.Buffer buffer = null;
            bool locked = false;
            try
            {
                uint stream, flags; long timestamp;
                Mf.Check(reader.ReadSample(Mf.VideoStream, 0, out stream, out flags, out timestamp, out sample));
                if ((flags & 1) != 0) throw new NotSupportedException("Video preview decoding failed.");
                if ((flags & 0x20) != 0) ReadFormat();
                Ended = (flags & 2) != 0;
                if (sample == null) return null;
                long duration;
                double seconds = timestamp / 10000000.0;
                double length = sample.GetSampleDuration(out duration) >= 0 && duration > 0 ? duration / 10000000.0 : frameDuration;
                Position = seconds + length;
                if (Position < keepFrom) return null; // Decode preroll without allocating bitmaps.
                Mf.Check(sample.ConvertToContiguousBuffer(out buffer));
                IntPtr data; uint capacity, count;
                Mf.Check(buffer.Lock(out data, out capacity, out count)); locked = true;
                int rowBytes = width * 4, pitch = Math.Abs(stride);
                if ((long)pitch * (height - 1) + rowBytes > count) throw new NotSupportedException("Incomplete video frame.");
                if (stride == rowBytes) Marshal.Copy(data, pixels, 0, pixels.Length);
                else for (int y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(data, (stride < 0 ? height - 1 - y : y) * pitch), pixels, y * rowBytes, rowBytes);
                BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, rowBytes);
                bitmap.Freeze();
                if (rotation == 90 || rotation == 180 || rotation == 270)
                {
                    bitmap = new TransformedBitmap(bitmap, new RotateTransform(rotation));
                    bitmap.Freeze();
                }
                return new VideoFrame { Bitmap = bitmap, Time = seconds, Duration = length };
            }
            finally
            {
                if (locked) buffer.Unlock();
                Mf.Release(buffer); Mf.Release(sample);
            }
        }

        public void Dispose()
        {
            Mf.Release(reader); reader = null;
            pixels = null;
            if (started) { started = false; Mf.MFShutdown(); }
        }
    }

    // Native ABI declarations follow the Windows SDK mfobjects/mfreadwrite interfaces.
    // Inherited IMFAttributes slots are flattened to preserve COM vtable order.
    internal static class Mf
    {
        internal const uint VideoStream = 0xFFFFFFFC;
        internal static Guid AdvancedProcessing = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3");
        internal static Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        internal static Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        internal static Guid VideoType = new Guid("73646976-0000-0010-8000-00aa00389b71");
        internal static Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");
        internal static Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        internal static Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        internal static Guid PixelAspectRatio = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        internal static Guid InterlaceMode = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        internal static Guid DefaultStride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        internal static Guid VideoRotation = new Guid("c380465d-2271-428c-9b83-ecea3b4a85c1");

        internal static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateAttributes(out Attributes attributes, uint size);
        [DllImport("mfplat.dll", ExactSpelling = true)] internal static extern int MFCreateMediaType(out MediaType type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int MFCreateSourceReaderFromURL(string path, Attributes attributes, out SourceReader reader);

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        internal struct PositionValue { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public long Value; }

        [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface Attributes
        {
            [PreserveSig] int GetItem(ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid key, out int type);
            [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr other, int mode, out int result);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
            [PreserveSig] int GetDouble(ref Guid key, out double value);
            [PreserveSig] int GetGUID(ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid key, out uint length);
            [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
            [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out uint size);
            [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(ref Guid key, ulong value);
            [PreserveSig] int SetDouble(ref Guid key, double value);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
            [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
            [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr destination);
        }

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface MediaType
        {
            [PreserveSig] int GetItem(ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid key, out int type);
            [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr other, int mode, out int result);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
            [PreserveSig] int GetDouble(ref Guid key, out double value);
            [PreserveSig] int GetGUID(ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid key, out uint length);
            [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
            [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out uint size);
            [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(ref Guid key, ulong value);
            [PreserveSig] int SetDouble(ref Guid key, double value);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
            [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
            [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr destination);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface Sample
        {
            [PreserveSig] int GetItem(ref Guid key, IntPtr value);
            [PreserveSig] int GetItemType(ref Guid key, out int type);
            [PreserveSig] int CompareItem(ref Guid key, IntPtr value, out int result);
            [PreserveSig] int Compare(IntPtr other, int mode, out int result);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
            [PreserveSig] int GetDouble(ref Guid key, out double value);
            [PreserveSig] int GetGUID(ref Guid key, out Guid value);
            [PreserveSig] int GetStringLength(ref Guid key, out uint length);
            [PreserveSig] int GetString(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);
            [PreserveSig] int GetBlobSize(ref Guid key, out uint size);
            [PreserveSig] int GetBlob(ref Guid key, IntPtr value, uint size, out uint length);
            [PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr value, out uint size);
            [PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr value);
            [PreserveSig] int SetItem(ref Guid key, IntPtr value);
            [PreserveSig] int DeleteItem(ref Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(ref Guid key, ulong value);
            [PreserveSig] int SetDouble(ref Guid key, double value);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
            [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob(ref Guid key, IntPtr value, uint size);
            [PreserveSig] int SetUnknown(ref Guid key, IntPtr value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetItemByIndex(uint index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IntPtr destination);

            [PreserveSig] int GetSampleFlags(out uint flags);
            [PreserveSig] int SetSampleFlags(uint flags);
            [PreserveSig] int GetSampleTime(out long time);
            [PreserveSig] int SetSampleTime(long time);
            [PreserveSig] int GetSampleDuration(out long duration);
            [PreserveSig] int SetSampleDuration(long duration);
            [PreserveSig] int GetBufferCount(out uint count);
            [PreserveSig] int GetBufferByIndex(uint index, out Buffer buffer);
            [PreserveSig] int ConvertToContiguousBuffer(out Buffer buffer);
            [PreserveSig] int AddBuffer(Buffer buffer);
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface Buffer
        {
            [PreserveSig] int Lock(out IntPtr data, out uint capacity, out uint length);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out uint length);
            [PreserveSig] int SetCurrentLength(uint length);
            [PreserveSig] int GetMaxLength(out uint length);
        }

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface SourceReader
        {
            [PreserveSig] int GetStreamSelection(uint stream, [MarshalAs(UnmanagedType.Bool)] out bool selected);
            [PreserveSig] int SetStreamSelection(uint stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
            [PreserveSig] int GetNativeMediaType(uint stream, uint index, out MediaType type);
            [PreserveSig] int GetCurrentMediaType(uint stream, out MediaType type);
            [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, MediaType type);
            [PreserveSig] int SetCurrentPosition(ref Guid format, ref PositionValue value);
            [PreserveSig] int ReadSample(uint stream, uint control, out uint actualStream, out uint flags, out long time, out Sample sample);
            [PreserveSig] int Flush(uint stream);
        }
    }
}

