using System;
using System.Runtime.InteropServices;

namespace CpuHealth
{
    internal struct Capacity
    {
        public readonly long Used;
        public readonly long Total;

        public Capacity(long used, long total)
        {
            Total = total;
            Used = Math.Max(0, Math.Min(used, total));
        }

        public double Percent { get { return 100.0 * Used / Total; } }
    }

    internal sealed class SystemMetrics
    {
        private ulong? lastIdle;
        private ulong lastTotal;

        public double? ReadCpuPercent()
        {
            ulong idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user))
            {
                lastIdle = null;
                return null;
            }

            ulong total = kernel + user;
            double? percent = null;
            if (lastIdle.HasValue && total > lastTotal && idle >= lastIdle.Value)
            {
                // The kernel counter includes idle time.
                double idleFraction = (double)(idle - lastIdle.Value) / (total - lastTotal);
                percent = 100 * Math.Max(0, 1 - idleFraction);
            }

            lastIdle = idle;
            lastTotal = total;
            return percent;
        }

        public static Capacity? ReadRam()
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (!GlobalMemoryStatusEx(ref memory) || memory.TotalPhysical == 0)
                return null;

            return new Capacity((long)memory.TotalPhysical - (long)memory.AvailablePhysical,
                (long)memory.TotalPhysical);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    }
}
