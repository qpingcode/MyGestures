using System.Runtime.InteropServices;
using System.Text;

namespace MyGestures.Utils;

internal static class FullscreenWindowDetector
{
    private const uint GetAncestorRoot = 2;
    private const int MaxClassNameLength = 256;
    internal const int SizeTolerancePixels = 4;

    public static bool IsForegroundFullscreen()
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        var root = Native.GetAncestor(window, GetAncestorRoot);
        if (root != IntPtr.Zero) window = root;
        if (IsDesktopShell(window)) return false;
        if (!Native.GetWindowRect(window, out var bounds)) return false;
        var monitor = Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;
        var info = new Native.MONITORINFO { Size = Marshal.SizeOf<Native.MONITORINFO>() };
        return Native.GetMonitorInfo(monitor, ref info) && CoversMonitor(bounds, info.Monitor);
    }

    internal static bool CoversMonitor(Native.RECT window, Native.RECT monitor)
        => Math.Abs(window.Left - monitor.Left) <= SizeTolerancePixels
           && Math.Abs(window.Top - monitor.Top) <= SizeTolerancePixels
           && Math.Abs(window.Right - monitor.Right) <= SizeTolerancePixels
           && Math.Abs(window.Bottom - monitor.Bottom) <= SizeTolerancePixels;

    internal static bool IsDesktopShellClassName(string className)
        => string.Equals(className, "Progman", StringComparison.Ordinal)
           || string.Equals(className, "WorkerW", StringComparison.Ordinal)
           || TaskbarWindowDetector.IsTaskbarClassName(className);

    private static bool IsDesktopShell(IntPtr window)
    {
        var className = new StringBuilder(MaxClassNameLength);
        return Native.GetClassName(window, className, className.Capacity) > 0
               && IsDesktopShellClassName(className.ToString());
    }
}
