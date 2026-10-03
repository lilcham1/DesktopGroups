using System.ComponentModel;
using System.Runtime.InteropServices;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>
/// Reports mouse button presses and releases anywhere on screen (a low-level mouse hook).
/// Events arrive on the thread that created this object, which must pump messages (the UI thread).
/// </summary>
sealed class GlobalMouse : IDisposable
{
    readonly LowLevelMouseProc _proc; // kept in a field: native code calls it, so it must not be collected
    readonly IntPtr _hook;

    public event Action<POINT>? Pressed;
    public event Action<POINT>? Released;

    public GlobalMouse()
    {
        _proc = OnMouse;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception();
    }

    IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var point = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data).pt;
            switch ((int)message)
            {
                case WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN:
                    Pressed?.Invoke(point);
                    break;
                case WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP:
                    Released?.Invoke(point);
                    break;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (!UnhookWindowsHookEx(_hook))
            throw new Win32Exception();
    }
}
