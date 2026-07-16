using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexUsageCompanion;

internal static class WorkingSetTrimmer
{
    public static void Trim()
    {
        using Process process = Process.GetCurrentProcess();
        _ = EmptyWorkingSet(process.Handle);
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint processHandle);
}
