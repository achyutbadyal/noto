using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Noto.Platform.Windows;

[SupportedOSPlatform("windows")]
static class WinNative
{
    public const int ErrorNotFound = 1168;
    public const uint WmQuit = 0x0012;
    public const uint WmHotkey = 0x0312;
    public const uint PmNoRemove = 0;
    public const uint SpiGetClientAreaAnimation = 0x1042;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FileTime LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredReadW(
        string target,
        uint type,
        uint flags,
        out IntPtr credential
    );

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    public static extern void CredFree(IntPtr buffer);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool PeekMessageW(
        out Msg msg,
        IntPtr hwnd,
        uint min,
        uint max,
        uint remove
    );

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        UIntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfoW(
        uint action,
        uint param,
        out int value,
        uint winIni
    );
}
