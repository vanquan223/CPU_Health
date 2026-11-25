using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CpuHealth
{
    internal sealed class PhysicalDiskCounters : IDisposable
    {
        private const uint MoreData = 0x800007d2;
        private const uint FormatDoubleWithoutScaling = 0x9200;
        private readonly IntPtr[] counters = new IntPtr[4];
        private IntPtr query;
        private bool initialized;

        public Dictionary<int, DiskReading> Read()
        {
            var output = new Dictionary<int, DiskReading>();
            if (!initialized)
            {
                initialized = true;
                if (PdhOpenQueryW(null, IntPtr.Zero, out query) == 0)
                {
                    string[] names = { "% Idle Time", "Disk Read Bytes/sec", "Disk Write Bytes/sec", "Avg. Disk sec/Transfer" };
                    for (int i = 0; i < names.Length; i++)
                        PdhAddEnglishCounterW(query, @"\PhysicalDisk(*)\" + names[i], IntPtr.Zero, out counters[i]);
                }
            }

            if (query != IntPtr.Zero && PdhCollectQueryData(query) == 0)
                for (int i = 0; i < counters.Length; i++)
                    if (counters[i] != IntPtr.Zero)
                        ReadCounter(counters[i], i, output);
            return output;
        }

        private static void ReadCounter(IntPtr counter, int index, Dictionary<int, DiskReading> output)
        {
            uint bytes = 0, count;
            if (PdhGetFormattedCounterArrayW(counter, FormatDoubleWithoutScaling, ref bytes, out count, IntPtr.Zero) != MoreData ||
                bytes == 0 || bytes > 1048576)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, FormatDoubleWithoutScaling, ref bytes, out count, buffer) != 0)
                    return;
                int stride = Marshal.SizeOf(typeof(CounterItem));
                for (int i = 0; i < count && i < bytes / stride; i++)
                {
                    var item = (CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * stride), typeof(CounterItem));
                    if (item.Value.Status > 1 || double.IsNaN(item.Value.Number) || double.IsInfinity(item.Value.Number))
                        continue;
                    string name = Marshal.PtrToStringUni(item.Name) ?? "";
                    int split = name.IndexOf(' ');
                    int number;
                    if (!int.TryParse(split < 0 ? name : name.Substring(0, split), NumberStyles.None,
                        CultureInfo.InvariantCulture, out number))
                        continue;
                    DiskReading reading;
                    if (!output.TryGetValue(number, out reading))
                        output[number] = reading = new DiskReading();
                    double value = Math.Max(0, item.Value.Number);
                    switch (index)
                    {
                        case 0: reading.Active = Math.Max(0, 100 - value); break;
                        case 1: reading.ReadRate = value; break;
                        case 2: reading.WriteRate = value; break;
                        case 3: reading.ResponseMs = value * 1000; break;
                    }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero)
                PdhCloseQuery(query);
            query = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct CounterValue
        {
            [FieldOffset(0)] public uint Status;
            [FieldOffset(8)] public double Number;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterItem
        {
            public IntPtr Name;
            public CounterValue Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhOpenQueryW(string source, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", ExactSpelling = true)]
        private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);
    }
}
