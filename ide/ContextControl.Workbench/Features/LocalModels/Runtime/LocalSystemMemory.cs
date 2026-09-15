using System.Runtime.InteropServices;

namespace ContextControl.Workbench.Services;

internal static class LocalSystemMemory
{
    public static (long? Total, long? Available) Read()
    {
        if (OperatingSystem.IsWindows())
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref memory)) return ((long)memory.TotalPhysical, (long)memory.AvailablePhysical);
        }
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var values = File.ReadLines("/proc/meminfo").Take(80).Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1].Trim().Split(' ')[0]);
                if (long.TryParse(values.GetValueOrDefault("MemTotal"), out var total)
                    && long.TryParse(values.GetValueOrDefault("MemAvailable"), out var free)) return (total * 1024, free * 1024);
            }
            catch (IOException) { }
        }
        return (null, null);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
