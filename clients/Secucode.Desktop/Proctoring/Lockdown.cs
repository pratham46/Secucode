using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Secucode.Desktop.Proctoring;

// Low-level keyboard hook that swallows Alt+Tab, Alt+Esc, Ctrl+Esc, the Windows keys and Alt+F4.
// Ctrl+Alt+Del can't be blocked by any app; the focus loss it causes is caught by the exam window.
public static class Lockdown
{
    // Kept in a static field so the GC can't collect the delegate while Windows still calls it.
    private static readonly LowLevelKeyboardProc HookProc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;

    public static bool IsEnabled => _hookId != IntPtr.Zero;

    public static void Enable()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, HookProc, GetModuleHandle(module.ModuleName), 0);
        if (_hookId == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error()); // lockdown must not silently be off
    }

    public static void Disable()
    {
        if (_hookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    public static bool ForegroundIsOurProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vk = Marshal.ReadInt32(lParam);
            var alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            var ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if ((vk == VK_TAB && alt) || (vk == VK_ESCAPE && (alt || ctrl)) || vk == VK_LWIN || vk == VK_RWIN || (vk == VK_F4 && alt))
                return 1;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MENU = 0x12, VK_CONTROL = 0x11;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
}
