using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sshocked.Services;

public sealed class WindowsPathInputReader(nint handle, uint originalMode) : IDisposable
{
    private const uint MouseInput = 0x0010;
    private const uint ExtendedFlags = 0x0080;
    private const uint LineInput = 0x0002;
    private const uint EchoInput = 0x0004;
    private const uint QuickEdit = 0x0040;
    private const uint VirtualTerminalInput = 0x0200;

    public static WindowsPathInputReader? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var input = GetStdHandle(-10);
        if (!GetConsoleMode(input, out var mode)) return null;
        // Quick Edit consumes clicks for selection. Restore it when the path editor closes.
        var editable = (mode | MouseInput | ExtendedFlags) & ~(LineInput | EchoInput | QuickEdit | VirtualTerminalInput);
        return SetConsoleMode(input, editable) ? new(input, mode) : null;
    }

    public PathInputEvent Read()
    {
        var record = new byte[20];
        while (true)
        {
            if (!ReadConsoleInput(handle, record, 1, out var read)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (read > 0 && PathInputEvent.DecodeWindowsRecord(record) is { } input) return input;
        }
    }

    public void Dispose() => SetConsoleMode(handle, originalMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int identifier);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(nint handle, uint mode);
    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadConsoleInput(nint handle, [Out] byte[] record, uint length, out uint read);
}
