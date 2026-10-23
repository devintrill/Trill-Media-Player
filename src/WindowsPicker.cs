using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Trill
{
    // The native Explorer dialog, shared by the file and folder commands.
    internal static class WindowsPicker
    {
        private const int Canceled = unchecked((int)0x800704C7);
        private const uint FileSystemPath = 0x80058000;
        private const uint NoChangeDirectory = 0x8;
        private const uint PickFolders = 0x20;
        private const uint ForceFileSystem = 0x40;
        private const uint AllowMultiSelect = 0x200;
        private const uint PathMustExist = 0x800;
        private const uint FileMustExist = 0x1000;
        private const uint NoDereferenceLinks = 0x100000;
        private const uint DontAddToRecent = 0x2000000;

        public static string[] Show(Window owner, bool selectFolder)
        {
            IFileOpenDialog dialog = CreateDialog(selectFolder);
            IShellItemArray results = null;
            try
            {
                int result = dialog.Show(new WindowInteropHelper(owner).Handle);
                if (result == Canceled) return null;
                Marshal.ThrowExceptionForHR(result);
                dialog.GetResults(out results);
                uint count;
                results.GetCount(out count);
                var paths = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    IShellItem item = null;
                    IntPtr path = IntPtr.Zero;
                    try
                    {
                        results.GetItemAt(i, out item);
                        item.GetDisplayName(FileSystemPath, out path);
                        string value = Marshal.PtrToStringUni(path);
                        if (!String.IsNullOrEmpty(value)) paths.Add(value);
                    }
                    finally
                    {
                        if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path);
                        if (item != null) Marshal.ReleaseComObject(item);
                    }
                }
                return paths.ToArray();
            }
            finally
            {
                if (results != null) Marshal.ReleaseComObject(results);
                Marshal.ReleaseComObject(dialog);
            }
        }

        internal static IFileOpenDialog CreateDialog(bool selectFolder)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                // Existing filesystem items only; leave the working directory and
                // Windows recent-document list alone. Final local-path validation
                // remains in MediaFiles for both native and Trill pickers.
                options |= ForceFileSystem | NoChangeDirectory | PathMustExist | FileMustExist | NoDereferenceLinks | DontAddToRecent;
                options &= ~(PickFolders | AllowMultiSelect);
                options |= selectFolder ? PickFolders : AllowMultiSelect;
                dialog.SetOptions(options);
                dialog.SetTitle(selectFolder ? "Open folder" : "Open files");
                if (!selectFolder)
                {
                    var filters = new FilterSpec[]
                    {
                        new FilterSpec("Media files", MediaFiles.FileDialogPattern),
                        new FilterSpec("All files", "*.*")
                    };
                    dialog.SetFileTypes((uint)filters.Length, filters);
                    dialog.SetFileTypeIndex(1);
                }
                return dialog;
            }
            catch
            {
                Marshal.ReleaseComObject(dialog);
                throw;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct FilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Pattern;
            public FilterSpec(string name, string pattern) { Name = name; Pattern = pattern; }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialog { }

        // IModalWindow + IFileDialog + IFileOpenDialog in native vtable order.
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem selection);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem result);
            void AddPlace(IShellItem place, int placement);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray results);
            void GetSelectedItems(out IShellItemArray selection);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint nameType, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemArray
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid iid, out IntPtr store);
            void GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr descriptions);
            void GetAttributes(int flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }
    }
}
