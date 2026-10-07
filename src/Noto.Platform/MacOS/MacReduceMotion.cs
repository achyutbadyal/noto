using System.Runtime.Versioning;
using Noto.Platform.Abstractions;

namespace Noto.Platform.MacOS;

// NSWorkspace.accessibilityDisplayShouldReduceMotion, polled because the change notification needs an ObjC block.
[SupportedOSPlatform("macos")]
public sealed class MacReduceMotion : IReduceMotion, IDisposable
{
    readonly Timer _poll;
    bool _last;

    public MacReduceMotion()
    {
        Native.dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 1);
        _last = Read();
        _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public bool IsEnabled => _last;
    public event Action? Changed;

    public void Dispose() => _poll.Dispose();

    void Poll()
    {
        var now = Read();
        if (now == _last) return;
        _last = now;
        Changed?.Invoke();
    }

    static bool Read()
    {
        var cls = Native.objc_getClass("NSWorkspace");
        if (cls == IntPtr.Zero) return false;
        var workspace = Native.MsgSend(cls, Native.sel_registerName("sharedWorkspace"));
        return workspace != IntPtr.Zero &&
               Native.MsgSendBool(workspace, Native.sel_registerName("accessibilityDisplayShouldReduceMotion")) != 0;
    }
}
