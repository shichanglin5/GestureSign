using System;
using System.Runtime.InteropServices;

namespace IWshRuntimeLibrary
{
    [ComImport]
    [Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IWshShell3
    {
        [DispId(0x03E8)]
        object CreateShortcut([In] string PathLink);
    }

    [ComImport]
    [Guid("F935DC21-1CF0-11D0-ADB9-00C04FD58A0B")]
    [CoClass(typeof(WshShellClass))]
    public interface WshShell : IWshShell3
    {
    }

    [ComImport]
    [Guid("F935DC21-1CF0-11D0-ADB9-00C04FD58A0B")]
    [ClassInterface(ClassInterfaceType.None)]
    public class WshShellClass
    {
    }

    [ComImport]
    [Guid("F935DC27-1CF0-11D0-ADB9-00C04FD58A0B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IWshShortcut
    {
        [DispId(0)]
        string FullName { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0)] get; }

        [DispId(0x03E8)]
        string Arguments { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03E8)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03E8)] set; }

        [DispId(0x03E9)]
        string Description { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03E9)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03E9)] set; }

        [DispId(0x03EA)]
        string Hotkey { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03EA)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03EA)] set; }

        [DispId(0x03EB)]
        string IconLocation { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03EB)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03EB)] set; }

        [DispId(0x03EC)]
        string RelativePath { [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03EC)] set; }

        [DispId(0x03ED)]
        string TargetPath { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03ED)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03ED)] set; }

        [DispId(0x03EE)]
        int WindowStyle { [DispId(0x03EE)] get; [param: In] [DispId(0x03EE)] set; }

        [DispId(0x03EF)]
        string WorkingDirectory { [return: MarshalAs(UnmanagedType.BStr)] [DispId(0x03EF)] get; [param: In, MarshalAs(UnmanagedType.BStr)] [DispId(0x03EF)] set; }

        [DispId(0x07D0)]
        void Load([In, MarshalAs(UnmanagedType.BStr)] string PathLink);

        [DispId(0x07D1)]
        void Save();
    }
}
