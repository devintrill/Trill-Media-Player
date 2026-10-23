using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Trill
{
    // Frosted-glass window backdrop, drawn by the desktop compositor rather than by
    // Trill. Windows 11 supplies the blur through DWM; on anything older every call
    // below fails harmlessly and the caller keeps its solid background.
    internal static class Glass
    {
        private const int ImmersiveDarkMode = 20;
        private const int BorderColor = 34;
        private const int CaptionColor = 35;
        private const int TextColor = 36;
        private const int SystemBackdropType = 38;
        private const int AcrylicBackdrop = 3;

        // DWM takes colours as COLORREF: 0x00BBGGRR, not the usual RGB order.
        private static int Colour(byte red, byte green, byte blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

        // Call once the window handle exists (SourceInitialized or later). Returns true
        // when the backdrop is live, which is the caller's cue to go transparent.
        public static bool Apply(Window window)
        {
            if (window == null || SystemParameters.HighContrast) return false;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;

            try
            {
                int dark = 1;
                DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref dark, sizeof(int));

                // Without this the title bar keeps the user's accent colour and fights
                // the frosted surface underneath it.
                int caption = Colour(0x14, 0x17, 0x1F);
                DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
                int text = Colour(0xED, 0xF0, 0xF5);
                DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
                int border = Colour(0x2E, 0x33, 0x40);
                DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));

                int backdrop = AcrylicBackdrop;
                if (DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, sizeof(int)) != 0) return false;

                // Negative margins extend the glass frame across the whole client area.
                Margins sheet = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(handle, ref sheet) != 0) return false;

                HwndSource source = HwndSource.FromHwnd(handle);
                if (source == null || source.CompositionTarget == null) return false;
                // Without this WPF paints opaque black over the backdrop.
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        // Hooks a window up so the backdrop is applied as soon as it has a handle.
        public static void Attach(Window window, Action<bool> applied)
        {
            if (window == null) return;
            window.SourceInitialized += delegate
            {
                bool ok = Apply(window);
                if (applied != null) applied(ok);
            };
        }
    }
}
