using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GestureSign.ControlPanel.Common
{
    /// <summary>
    /// IShellLink COM interface for creating Windows shortcuts (.lnk files)
    /// This is more reliable than WshShell in .NET 8 environments
    /// </summary>
    internal static class ShellLinkInterop
    {
        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFileName);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        [ClassInterface(ClassInterfaceType.None)]
        private class ShellLink
        {
        }

        /// <summary>
        /// Creates a Windows shortcut (.lnk file) using IShellLink
        /// </summary>
        /// <param name="lnkPath">Path where the shortcut will be created</param>
        /// <param name="targetPath">Target executable or file path</param>
        /// <param name="arguments">Command line arguments (optional)</param>
        /// <param name="description">Shortcut description (optional)</param>
        /// <param name="workingDirectory">Working directory (optional)</param>
        /// <param name="windowStyle">Window show command (1=Normal, 3=Maximized, 7=Minimized)</param>
        public static void CreateShortcut(
            string lnkPath,
            string targetPath,
            string? arguments = null,
            string? description = null,
            string? workingDirectory = null,
            int windowStyle = 1)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();

                link.SetPath(targetPath);

                if (!string.IsNullOrEmpty(arguments))
                    link.SetArguments(arguments);

                if (!string.IsNullOrEmpty(description))
                    link.SetDescription(description);

                if (!string.IsNullOrEmpty(workingDirectory))
                    link.SetWorkingDirectory(workingDirectory);

                link.SetShowCmd(windowStyle);

                var file = (IPersistFile)link;
                file.Save(lnkPath, true);

                // Release COM objects
                Marshal.FinalReleaseComObject(file);
                Marshal.FinalReleaseComObject(link);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Failed to create shortcut at {lnkPath}", ex);
            }
        }

        /// <summary>
        /// Reads the target path from a Windows shortcut (.lnk file)
        /// </summary>
        /// <param name="lnkPath">Path to the .lnk file</param>
        /// <returns>The target path, or null if the shortcut cannot be read</returns>
        public static string? GetShortcutTarget(string lnkPath)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                var file = (IPersistFile)link;

                file.Load(lnkPath, 0);

                var pathBuffer = new StringBuilder(260);
                link.GetPath(pathBuffer, pathBuffer.Capacity, IntPtr.Zero, 0);

                var targetPath = pathBuffer.ToString();

                // Release COM objects
                Marshal.FinalReleaseComObject(file);
                Marshal.FinalReleaseComObject(link);

                return string.IsNullOrEmpty(targetPath) ? null : targetPath;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }
}
