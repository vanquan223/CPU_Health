using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CpuHealth
{
    internal sealed class DiskReading
    {
        public double? Active, ReadRate, WriteRate, ResponseMs;
        public bool FromVolumes;
    }

    internal sealed class PhysicalDisk
    {
        public readonly int Number;
        public readonly List<string> Letters = new List<string>();
        public readonly List<string> Volumes = new List<string>();
        public string Model, Type;
        public long? Capacity, Formatted;
        public bool ContainsWindows;
        public bool? HasPageFile;

        public PhysicalDisk(int number) { Number = number; }
        public string Name { get { return "Disk " + Number; } }
        public string Label { get { return Letters.Count == 0 ? Name : Name + " (" + string.Join(" ", Letters) + ")"; } }
        public string DevicePath { get { return @"\\.\PhysicalDrive" + Number.ToString(CultureInfo.InvariantCulture); } }
    }

    internal sealed class DiskSnapshot
    {
        public readonly PhysicalDisk Disk;
        public readonly DiskReading Reading;

        public DiskSnapshot(PhysicalDisk disk, DiskReading reading) { Disk = disk; Reading = reading; }
    }

    internal sealed class DiskMonitor : IDisposable
    {
        private const uint DiskPerformance = 0x00070020;
        private const uint DiskPerformanceOff = 0x00070060;
        private const uint DiskGeometryEx = 0x000700a0;
        private const uint StorageQueryProperty = 0x002d1400;
        private const uint VolumeDiskExtents = 0x00560000;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<string, NativeCounter> counters = new Dictionary<string, NativeCounter>();
        private readonly PhysicalDiskCounters pdh = new PhysicalDiskCounters();
        private readonly byte[] buffer = new byte[8192];
        private SortedDictionary<int, PhysicalDisk> disks;
        private long nextDiscovery;

        public DiskSnapshot[] Read()
        {
            if (disks == null || clock.ElapsedMilliseconds >= nextDiscovery)
            {
                disks = Discover();
                nextDiscovery = clock.ElapsedMilliseconds + 10000;
                var paths = new HashSet<string>(disks.Values.SelectMany(d => d.Volumes.Concat(new[] { d.DevicePath })));
                foreach (string old in counters.Keys.Where(path => !paths.Contains(path) || !counters[path].Available).ToArray())
                {
                    counters[old].Dispose();
                    counters.Remove(old);
                }
            }

            var performance = pdh.Read();
            var output = new List<DiskSnapshot>();
            foreach (int number in disks.Keys.Union(performance.Keys).OrderBy(n => n))
            {
                PhysicalDisk disk;
                if (!disks.TryGetValue(number, out disk))
                    disk = new PhysicalDisk(number);

                DiskReading reading;
                if (!performance.TryGetValue(number, out reading) || !reading.Active.HasValue)
                {
                    Delta? physical = Sample(disk.DevicePath);
                    if (physical.HasValue)
                        reading = Combine(new[] { physical.Value }, false);
                    else
                    {
                        var volumes = disk.Volumes.Select(Sample).Where(sample => sample.HasValue)
                            .Select(sample => sample.Value).ToArray();
                        if (volumes.Length != 0)
                            reading = Combine(volumes, true);
                    }
                }
                output.Add(new DiskSnapshot(disk, reading));
            }
            return output.ToArray();
        }

        public void Dispose()
        {
            pdh.Dispose();
            foreach (NativeCounter counter in counters.Values)
                counter.Dispose();
            counters.Clear();
        }

        private Delta? Sample(string path)
        {
            NativeCounter counter;
            if (!counters.TryGetValue(path, out counter))
            {
                counter = new NativeCounter(Open(path));
                counters.Add(path, counter);
            }
            return counter.Read(buffer);
        }

        private static DiskReading Combine(Delta[] samples, bool fromVolumes)
        {
            double operations = samples.Sum(sample => sample.Operations);
            return new DiskReading
            {
                // Idle intervals from separate volumes can overlap. The largest active fraction is a lower bound.
                Active = samples.Max(sample => sample.Active),
                ReadRate = samples.Sum(sample => sample.ReadRate),
                WriteRate = samples.Sum(sample => sample.WriteRate),
                ResponseMs = operations > 0 ? samples.Sum(sample => sample.ServiceMs) / operations : 0,
                FromVolumes = fromVolumes
            };
        }

        private SortedDictionary<int, PhysicalDisk> Discover()
        {
            var all = new SortedDictionary<int, PhysicalDisk>();
            string system = System.IO.Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
            var pages = new List<string>();
            bool pageFilesKnown = K32EnumPageFilesW((context, info, name) => { pages.Add(name); return true; }, IntPtr.Zero);

            var devices = new char[32768];
            int names = QueryDosDeviceW(null, devices, devices.Length);
            foreach (string path in new string(devices, 0, names).Split('\0'))
            {
                int number;
                if (!path.StartsWith("PhysicalDrive", StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(path.Substring(13), out number) || number < 0)
                    continue;
                var disk = new PhysicalDisk(number);
                using (SafeFileHandle handle = Open(disk.DevicePath))
                    if (!handle.IsInvalid)
                        ReadDescriptor(disk, handle);
                all[number] = disk;
            }

            var volume = new StringBuilder(260);
            IntPtr enumeration = FindFirstVolumeW(volume, volume.Capacity);
            if (enumeration == new IntPtr(-1))
                return all;

            try
            {
                do
                {
                    string root = volume.ToString();
                    string path = root.TrimEnd('\\');
                    using (SafeFileHandle handle = Open(path))
                    {
                        if (handle.IsInvalid)
                            continue;
                        int length = Control(handle, VolumeDiskExtents, null, buffer);
                        if (length < 32)
                            continue;
                        int count = BitConverter.ToInt32(buffer, 0);
                        if (count < 1 || count > (length - 8) / 24)
                            continue;

                        int number = BitConverter.ToInt32(buffer, 8);
                        if (number < 0 || Enumerable.Range(1, count - 1).Any(i => BitConverter.ToInt32(buffer, 8 + i * 24) != number))
                            continue; // A spanned volume cannot be attributed to a single disk.
                        long volumeBytes = Enumerable.Range(0, count).Sum(i => BitConverter.ToInt64(buffer, 24 + i * 24));

                        PhysicalDisk disk;
                        if (!all.TryGetValue(number, out disk))
                            all[number] = disk = new PhysicalDisk(number);
                        disk.Volumes.Add(path);
                        foreach (string letter in DriveLetters(root))
                        {
                            if (!disk.Letters.Contains(letter))
                                disk.Letters.Add(letter);
                            disk.ContainsWindows |= string.Equals(letter, system, StringComparison.OrdinalIgnoreCase);
                        }
                        if (pageFilesKnown)
                            disk.HasPageFile = disk.HasPageFile.GetValueOrDefault() || pages.Any(page => page.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                                disk.Letters.Any(letter => page.StartsWith(letter + "\\", StringComparison.OrdinalIgnoreCase)));

                        if (disk.Capacity == null || disk.Model == null || disk.Type == null)
                            ReadDescriptor(disk, handle);

                        ulong free, total, unused;
                        if (volumeBytes > 0 && GetDiskFreeSpaceExW(root, out free, out total, out unused) && total > 0)
                            disk.Formatted = disk.Formatted.GetValueOrDefault() + volumeBytes;
                    }
                } while (FindNextVolumeW(enumeration, volume, volume.Capacity));
            }
            finally { FindVolumeClose(enumeration); }

            foreach (PhysicalDisk disk in all.Values)
                disk.Letters.Sort(StringComparer.OrdinalIgnoreCase);
            return all;
        }

        private static IEnumerable<string> DriveLetters(string volume)
        {
            var paths = new char[256];
            int count;
            if (!GetVolumePathNamesForVolumeNameW(volume, paths, paths.Length, out count))
            {
                if (count <= paths.Length || count > 32768)
                    return new string[0];
                paths = new char[count];
                if (!GetVolumePathNamesForVolumeNameW(volume, paths, paths.Length, out count))
                    return new string[0];
            }
            return new string(paths, 0, Math.Min(count, paths.Length)).Split('\0')
                .Where(path => path.Length == 3 && path[1] == ':' && path[2] == '\\')
                .Select(path => path.Substring(0, 2));
        }

        private void ReadDescriptor(PhysicalDisk disk, SafeFileHandle handle)
        {
            if (disk.Capacity == null && Control(handle, DiskGeometryEx, null, buffer) >= 32)
            {
                long bytes = BitConverter.ToInt64(buffer, 24);
                if (bytes > 0)
                    disk.Capacity = bytes;
            }
            int length = Control(handle, StorageQueryProperty, new byte[12], buffer); // StorageDeviceProperty
            if (length < 36)
                return;

            int bus = BitConverter.ToInt32(buffer, 28);
            string vendor = DescriptorText(buffer, length, 12);
            string product = DescriptorText(buffer, length, 16);
            if (!string.IsNullOrEmpty(product))
                disk.Model = product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ? product : vendor + " " + product;

            string[] buses = { null, "SCSI", "ATAPI", "ATA", "IEEE 1394", "SSA", "Fibre", "USB", "RAID", "iSCSI",
                "SAS", "SATA", "SD", "MMC", "Ảo", "Ảo", "Storage Spaces", "NVMe", "SCM", "UFS", "NVMe-oF" };
            string busName = bus > 0 && bus < buses.Length ? buses[bus] : null;
            string media = null;
            if (bus == 19 || bus == 17)
                media = "SSD";
            else if (bus != 12 && bus != 13 && bus != 14 && bus != 15 && bus != 16 && bus != 18 &&
                Control(handle, StorageQueryProperty, new byte[] { 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, buffer) >= 9)
                media = buffer[8] == 0 ? "SSD" : "HDD";
            disk.Type = media != null && busName != null ? media + " (" + busName + ")" : media ?? busName;
        }

        private static string DescriptorText(byte[] data, int length, int field)
        {
            int start = BitConverter.ToInt32(data, field);
            if (start <= 0 || start >= length)
                return "";
            int end = Array.IndexOf(data, (byte)0, start, length - start);
            return Encoding.ASCII.GetString(data, start, (end < 0 ? length : end) - start).Trim();
        }

        private static SafeFileHandle Open(string path) { return CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero); }

        private static int Control(SafeFileHandle handle, uint code, byte[] input, byte[] output)
        {
            int size;
            return DeviceIoControl(handle, code, input, input == null ? 0 : input.Length,
                output, output == null ? 0 : output.Length, out size, IntPtr.Zero) ? size : -1;
        }

        private struct Delta
        {
            public double Active, ReadRate, WriteRate, Operations, ServiceMs;
        }

        private sealed class NativeCounter : IDisposable
        {
            private readonly SafeFileHandle handle;
            private bool enabled;
            private long[] last;

            public NativeCounter(SafeFileHandle handle) { this.handle = handle; }
            public bool Available { get { return !handle.IsInvalid; } }

            public Delta? Read(byte[] data)
            {
                if (!Available || Control(handle, DiskPerformance, null, data) < 64)
                {
                    last = null;
                    return null;
                }

                // Hold one enable reference between samples. Balance the extra reference on subsequent reads.
                if (enabled)
                    Control(handle, DiskPerformanceOff, null, null);
                enabled = true;
                long[] before = last;
                last = new[] { BitConverter.ToInt64(data, 0), BitConverter.ToInt64(data, 8), BitConverter.ToInt64(data, 16),
                    BitConverter.ToInt64(data, 24), BitConverter.ToInt64(data, 32), BitConverter.ToInt64(data, 56),
                    (long)BitConverter.ToUInt32(data, 40), (long)BitConverter.ToUInt32(data, 44) };
                if (before == null || Enumerable.Range(0, 6).Any(i => last[i] < before[i]) || last[5] == before[5])
                    return null;

                double seconds = (last[5] - before[5]) / 10000000.0;
                double idle = (last[4] - before[4]) / 10000000.0;
                return new Delta
                {
                    Active = 100 * (1 - Math.Max(0, Math.Min(1, idle / seconds))),
                    ReadRate = (last[0] - before[0]) / seconds,
                    WriteRate = (last[1] - before[1]) / seconds,
                    ServiceMs = ((last[2] - before[2]) + (last[3] - before[3])) / 10000.0,
                    Operations = (double)unchecked((uint)(last[6] - before[6])) + unchecked((uint)(last[7] - before[7]))
                };
            }

            public void Dispose()
            {
                if (enabled)
                    Control(handle, DiskPerformanceOff, null, null);
                handle.Dispose();
            }
        }

        private delegate bool PageFileCallback(IntPtr context, IntPtr info, [MarshalAs(UnmanagedType.LPWStr)] string name);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern bool K32EnumPageFilesW(PageFileCallback callback, IntPtr context);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int QueryDosDeviceW(string name, [Out] char[] target, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr FindFirstVolumeW(StringBuilder name, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool FindNextVolumeW(IntPtr enumeration, StringBuilder name, int length);
        [DllImport("kernel32.dll")]
        private static extern bool FindVolumeClose(IntPtr enumeration);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool GetVolumePathNamesForVolumeNameW(string name, [Out] char[] paths, int length, out int returned);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool GetDiskFreeSpaceExW(string root, out ulong free, out ulong total, out ulong totalFree);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll")]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
            [Out] byte[] output, int outputSize, out int returned, IntPtr overlapped);
    }
}
