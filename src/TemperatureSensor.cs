using System;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;

namespace CpuHealth
{
    internal sealed class TemperatureReading
    {
        public readonly double Celsius;
        public readonly bool IsCpu;

        public TemperatureReading(double celsius, bool isCpu)
        {
            Celsius = celsius;
            IsCpu = isCpu;
        }
    }

    internal static class TemperatureSensor
    {
        public static TemperatureReading Read()
        {
            double? cpu = ReadCpu();
            if (cpu.HasValue)
                return new TemperatureReading(cpu.Value, true);

            double? zone = ReadZone(@"root\wmi", "MSAcpi_ThermalZoneTemperature", "CurrentTemperature", 10)
                ?? ReadZone(@"root\cimv2", "Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature", 1);
            return zone.HasValue ? new TemperatureReading(zone.Value, false) : null;
        }

        private static double? ReadCpu()
        {
            double? best = null;
            int bestPriority = 0;
            Query(@"root\LibreHardwareMonitor",
                "SELECT Identifier, Name, Value FROM Sensor WHERE SensorType = 'Temperature'", row =>
                {
                    string id = row["Identifier"] as string ?? "";
                    if (!id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase) &&
                        !id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase) &&
                        !id.StartsWith("/cpu/", StringComparison.OrdinalIgnoreCase))
                        return;

                    double? value = ValidCelsius(row["Value"], 1, 0);
                    if (!value.HasValue)
                        return;

                    string name = row["Name"] as string ?? "";
                    bool package = name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0;
                    int priority = package ? 2 : 1;
                    if (priority > bestPriority || (priority == bestPriority && value > best))
                    {
                        best = value;
                        bestPriority = priority;
                    }
                });
            return best;
        }

        private static double? ReadZone(string scope, string className, string property, double divisor)
        {
            double? highest = null;
            Query(scope, "SELECT " + property + " FROM " + className, row =>
            {
                double? value = ValidCelsius(row[property], divisor, 273.15);
                if (value.HasValue && (!highest.HasValue || value > highest))
                    highest = value;
            });
            return highest;
        }

        private static double? ValidCelsius(object raw, double divisor, double offset)
        {
            double number;
            if (!double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number))
                return null;

            double celsius = number / divisor - offset;
            return celsius >= -50 && celsius <= 150 ? celsius : (double?)null;
        }

        private static void Query(string scope, string query, Action<ManagementBaseObject> read)
        {
            var options = new EnumerationOptions
            {
                BlockSize = 16,
                ReturnImmediately = true,
                Rewindable = false,
                Timeout = TimeSpan.FromSeconds(2)
            };

            try
            {
                using (var search = new ManagementObjectSearcher(scope, query, options))
                using (ManagementObjectCollection rows = search.Get())
                    foreach (ManagementBaseObject row in rows)
                        using (row)
                            read(row);
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            catch (COMException) { }
        }
    }
}
