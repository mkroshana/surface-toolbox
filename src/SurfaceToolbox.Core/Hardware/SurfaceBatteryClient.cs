using System;
using System.Runtime.InteropServices;
using System.Text;
using SurfaceToolbox.Core.Models;

namespace SurfaceToolbox.Core.Hardware
{
    /// <summary>
    /// Reads the battery through the standard Windows battery driver interface
    /// (IOCTL_BATTERY_QUERY_*). This is read-only and needs no Surface-specific driver.
    /// It is used instead of the root\WMI battery classes because BatteryStaticData, the only
    /// WMI source of design capacity, fails with "Generic failure" on the Surface Pro 9.
    /// </summary>
    public sealed class SurfaceBatteryClient
    {
        private static readonly Guid GUID_DEVICE_BATTERY = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint CR_SUCCESS = 0;
        private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

        private const uint IOCTL_BATTERY_QUERY_TAG = 0x00294040;
        private const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x00294044;
        private const uint IOCTL_BATTERY_QUERY_STATUS = 0x0029404C;

        // BATTERY_QUERY_INFORMATION_LEVEL
        private const int BatteryInformation = 0;
        private const int BatteryTemperature = 2;
        private const int BatteryEstimatedTime = 3;

        // BATTERY_STATUS.PowerState
        private const uint BATTERY_POWER_ON_LINE = 0x1;
        private const uint BATTERY_DISCHARGING = 0x2;
        private const uint BATTERY_CHARGING = 0x4;

        private const uint BATTERY_CAPACITY_RELATIVE = 0x40000000;
        private const uint BATTERY_UNKNOWN = 0xFFFFFFFF;
        private const int BATTERY_UNKNOWN_RATE = unchecked((int)0x80000000);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize,
            [Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_List_SizeW(out uint pulLen, ref Guid InterfaceClassGuid, string? pDeviceID, uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_ListW(ref Guid InterfaceClassGuid, string? pDeviceID, [Out] char[] Buffer, uint BufferLen, uint ulFlags);

        /// <summary>Takes one reading. Returns null when no battery device can be opened or it has no battery.</summary>
        public BatterySnapshot? Read()
        {
            string? path = ResolveBatteryPath();
            if (path == null)
                return null;

            IntPtr handle = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return null;

            try
            {
                // The tag identifies the battery currently in the slot; every other query needs it.
                byte[] wait = BitConverter.GetBytes(0u);
                byte[] tagBuffer = new byte[4];
                if (!DeviceIoControl(handle, IOCTL_BATTERY_QUERY_TAG, wait, (uint)wait.Length, tagBuffer, (uint)tagBuffer.Length, out _, IntPtr.Zero))
                    return null;

                uint tag = BitConverter.ToUInt32(tagBuffer, 0);
                if (tag == 0)
                    return null;

                var snapshot = new BatterySnapshot();
                bool relativeCapacity = false;

                // BATTERY_INFORMATION: Capabilities(4) Technology(1) Reserved(3) Chemistry(4)
                // DesignedCapacity(4) FullChargedCapacity(4) DefaultAlert1(4) DefaultAlert2(4) CriticalBias(4) CycleCount(4)
                byte[] info = new byte[36];
                if (QueryInformation(handle, tag, BatteryInformation, info) >= 36)
                {
                    relativeCapacity = (BitConverter.ToUInt32(info, 0) & BATTERY_CAPACITY_RELATIVE) != 0;
                    snapshot.Chemistry = Encoding.ASCII.GetString(info, 8, 4).TrimEnd('\0', ' ');

                    if (!relativeCapacity)
                    {
                        snapshot.DesignWh = ToWattHours(BitConverter.ToUInt32(info, 12));
                        snapshot.FullChargeWh = ToWattHours(BitConverter.ToUInt32(info, 16));
                    }

                    uint cycles = BitConverter.ToUInt32(info, 32);
                    if (cycles != 0 && cycles != BATTERY_UNKNOWN)
                        snapshot.CycleCount = (int)cycles;
                }

                // BATTERY_WAIT_STATUS in (tag, then zeros = answer immediately); BATTERY_STATUS out:
                // PowerState(4) Capacity(4) Voltage(4) Rate(4, signed)
                byte[] waitStatus = new byte[20];
                BitConverter.GetBytes(tag).CopyTo(waitStatus, 0);
                byte[] status = new byte[16];
                if (DeviceIoControl(handle, IOCTL_BATTERY_QUERY_STATUS, waitStatus, (uint)waitStatus.Length, status, (uint)status.Length, out uint statusBytes, IntPtr.Zero) && statusBytes >= 16)
                {
                    uint powerState = BitConverter.ToUInt32(status, 0);
                    snapshot.IsOnAcPower = (powerState & BATTERY_POWER_ON_LINE) != 0;
                    snapshot.IsCharging = (powerState & BATTERY_CHARGING) != 0;
                    snapshot.IsDischarging = (powerState & BATTERY_DISCHARGING) != 0;

                    if (!relativeCapacity)
                        snapshot.RemainingWh = ToWattHours(BitConverter.ToUInt32(status, 4));

                    uint voltage = BitConverter.ToUInt32(status, 8);
                    if (voltage != BATTERY_UNKNOWN)
                        snapshot.VoltageV = voltage / 1000.0;

                    int rate = BitConverter.ToInt32(status, 12);
                    if (rate != BATTERY_UNKNOWN_RATE && !relativeCapacity)
                        snapshot.RateW = rate / 1000.0;
                }

                // Temperature is in tenths of a kelvin; many batteries, this one included, do not report it.
                byte[] temperature = new byte[4];
                if (QueryInformation(handle, tag, BatteryTemperature, temperature) >= 4)
                {
                    uint tenthsKelvin = BitConverter.ToUInt32(temperature, 0);
                    if (tenthsKelvin != 0 && tenthsKelvin != BATTERY_UNKNOWN)
                        snapshot.TemperatureCelsius = Math.Round(tenthsKelvin / 10.0 - 273.15, 1);
                }

                // Seconds of runtime at the present drain; only meaningful while discharging.
                byte[] time = new byte[4];
                if (snapshot.IsDischarging && QueryInformation(handle, tag, BatteryEstimatedTime, time) >= 4)
                {
                    uint seconds = BitConverter.ToUInt32(time, 0);
                    if (seconds != 0 && seconds != BATTERY_UNKNOWN)
                        snapshot.EstimatedRuntime = TimeSpan.FromSeconds(seconds);
                }

                return snapshot;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // Returns the number of bytes the driver wrote, or 0 if this information level is not supported.
        private static uint QueryInformation(IntPtr handle, uint tag, int level, byte[] output)
        {
            // BATTERY_QUERY_INFORMATION: BatteryTag(4) InformationLevel(4) AtRate(4)
            byte[] query = new byte[12];
            BitConverter.GetBytes(tag).CopyTo(query, 0);
            BitConverter.GetBytes(level).CopyTo(query, 4);

            return DeviceIoControl(handle, IOCTL_BATTERY_QUERY_INFORMATION, query, (uint)query.Length, output, (uint)output.Length, out uint written, IntPtr.Zero)
                ? written
                : 0;
        }

        private static double? ToWattHours(uint milliwattHours)
        {
            return milliwattHours == BATTERY_UNKNOWN ? null : milliwattHours / 1000.0;
        }

        private static string? ResolveBatteryPath()
        {
            try
            {
                Guid guid = GUID_DEVICE_BATTERY;
                if (CM_Get_Device_Interface_List_SizeW(out uint length, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || length <= 1)
                    return null;

                char[] buffer = new char[length];
                if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
                    return null;

                string[] entries = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                return entries.Length > 0 ? entries[0] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
