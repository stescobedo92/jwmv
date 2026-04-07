using System.Runtime.InteropServices;

namespace Jwmv.Infrastructure.Windows;

internal static class EnvironmentBroadcast
{
    private const int HwndBroadcast = 0xffff;
    private const int WmSettingChange = 0x001A;
    private const int SmtoAbortIfHung = 0x0002;

    public static void Notify()
    {
        SendMessageTimeout((nint)HwndBroadcast, WmSettingChange, nint.Zero, "Environment", SmtoAbortIfHung, 100, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string lParam, int fuFlags, int uTimeout, out nint lpdwResult);
}
