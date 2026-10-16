using System;
using System.Runtime.InteropServices;

namespace SurfaceFanControl.Core.Hardware
{
    public sealed class SurfaceSamClient : IDisposable
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint IOCTL_SSH_SEND_COMMAND_TO_TARGET = 0x04001C48;
        private const uint IOCTL_SMF_CHANNEL_ACTION = 0x1FAA000C;
        private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;
        private const uint CR_SUCCESS = 0x00000000;

        public static readonly Guid GUID_DEVINTERFACE_SURFACE_SERIAL_HUB = new("99bbc337-05d6-4819-b648-39589febb93e");
        public static readonly Guid GUID_DEVINTERFACE_SMF_CORE           = new("b98aea2e-7ee8-403c-8232-f0d48d5dd338");

        public static readonly Guid GUID_SMF_FAN_INTERFACE   = new("b620798f-6fb0-42ef-b9b3-d2370402e089");
        public static readonly Guid GUID_SMF_POWER_LIMITS    = new("fb805e77-616e-4ea8-bac1-6859a6c2d4b1");
        public static readonly Guid GUID_SMF_PROCESSOR_POWER = new("049e36f2-906d-47f2-aca4-610552bfb1fa");
        public static readonly Guid GUID_SMF_SYSTEM_SLIDER   = new("87008365-ea3a-461c-b83d-6ac6e8db2f8a");

        private const string FallbackSshPath = @"\\?\ACPI#MSHW0084#2&daba3ff&0#{99bbc337-05d6-4819-b648-39589febb93e}";
        private const string FallbackSmfPath = @"\\?\{16C280BF-D438-43C1-BBF3-688CA0D43E2F}#SurfaceSystemManagementFramework#3&1e75d51e&0&SID_DEVICE_01#{b98aea2e-7ee8-403c-8232-f0d48d5dd338}";

        private readonly object _syncRoot = new();
        private IntPtr _sshHandle = IntPtr.Zero;
        private IntPtr _smfHandle = IntPtr.Zero;
        private bool _disposed;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[] lpInBuffer,
            uint nInBufferSize,
            [Out] byte[]? lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_List_SizeW(
            out uint pulLen,
            ref Guid InterfaceClassGuid,
            string? pDeviceID,
            uint ulFlags);

        [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_Interface_ListW(
            ref Guid InterfaceClassGuid,
            string? pDeviceID,
            [Out] char[] Buffer,
            uint BufferLen,
            uint ulFlags);

        public SurfaceSamClient()
        {
            EnsureSmfHandle();
            EnsureSshHandle();
        }

        public bool IsDirectSamAvailable
        {
            get
            {
                lock (_syncRoot)
                {
                    return EnsureSshHandle();
                }
            }
        }

        public bool IsSmfTelemetryAvailable
        {
            get
            {
                lock (_syncRoot)
                {
                    return EnsureSmfHandle();
                }
            }
        }

        public bool TryReadFanRpm(out int rpm, byte fanInstance = 1)
        {
            rpm = 0;
            lock (_syncRoot)
            {
                if (EnsureSshHandle())
                {
                    // TC=0x05 (Fan), TID=0x01, IID=fanInstance, HasResponse=0x01, CID=0x01, PayloadLen=0x0000
                    byte[] inBuf = { 0x05, 0x01, fanInstance, 0x01, 0x01, 0x00, 0x00 };
                    byte[] outBuf = new byte[2];
                    if (DeviceIoControl(_sshHandle, IOCTL_SSH_SEND_COMMAND_TO_TARGET, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out uint bytesRet, IntPtr.Zero) && bytesRet >= 2)
                    {
                        rpm = BitConverter.ToUInt16(outBuf, 0);
                        return true;
                    }
                }
            }

            // Fallback to SMF Core target RPM channel (0x65) when running unprivileged
            if (TryReadSmfChannel(GUID_SMF_FAN_INTERFACE, 0x65, out long smfRpm) && smfRpm >= 0)
            {
                rpm = (int)smfRpm;
                return true;
            }

            return false;
        }

        public bool TrySetFanRpm(ushort targetRpm, byte fanInstance = 1)
        {
            lock (_syncRoot)
            {
                if (!EnsureSshHandle())
                    return false;

                byte lo = (byte)(targetRpm & 0xFF);
                byte hi = (byte)((targetRpm >> 8) & 0xFF);
                // TC=0x05 (Fan), TID=0x01, IID=fanInstance, HasResponse=0x00, CID=0x0B, PayloadLen=0x0002
                byte[] inBuf = { 0x05, 0x01, fanInstance, 0x00, 0x0B, 0x02, 0x00, lo, hi };
                return DeviceIoControl(_sshHandle, IOCTL_SSH_SEND_COMMAND_TO_TARGET, inBuf, (uint)inBuf.Length, null, 0, out _, IntPtr.Zero);
            }
        }

        public bool TrySetFanProfile(byte profile, byte fanInstance = 1)
        {
            lock (_syncRoot)
            {
                if (!EnsureSshHandle())
                    return false;

                // TC=0x05 (Fan), TID=0x01, IID=fanInstance, HasResponse=0x00, CID=0x0E, PayloadLen=0x0001
                byte[] inBuf = { 0x05, 0x01, fanInstance, 0x00, 0x0E, 0x01, 0x00, profile };
                return DeviceIoControl(_sshHandle, IOCTL_SSH_SEND_COMMAND_TO_TARGET, inBuf, (uint)inBuf.Length, null, 0, out _, IntPtr.Zero);
            }
        }

        public bool TryReadSmfFanProfile(out int profile)
        {
            profile = 2;
            if (TryReadSmfChannel(GUID_SMF_FAN_INTERFACE, 0xC9, out long val))
            {
                profile = (int)val;
                return true;
            }
            return false;
        }

        public bool TryReadPowerLimits(out double pl1Watts, out double pl2Watts)
        {
            pl1Watts = 0;
            pl2Watts = 0;
            bool ok1 = TryReadSmfChannel(GUID_SMF_POWER_LIMITS, 0x01, out long pl1Mw);
            bool ok2 = TryReadSmfChannel(GUID_SMF_POWER_LIMITS, 0x02, out long pl2Mw);
            if (ok1 && pl1Mw > 0) pl1Watts = Math.Round(pl1Mw / 1000.0, 1);
            if (ok2 && pl2Mw > 0) pl2Watts = Math.Round(pl2Mw / 1000.0, 1);
            return ok1 || ok2;
        }

        public bool TryReadPackagePower(out double packageWatts)
        {
            packageWatts = 0;
            if (TryReadSmfChannel(GUID_SMF_PROCESSOR_POWER, 0x01, out long rawMw) && rawMw > 0)
            {
                packageWatts = Math.Round(rawMw / 1000.0, 1);
                return true;
            }
            return false;
        }

        public bool TryReadSmfChannel(Guid interfaceGuid, uint channelId, out long value)
        {
            value = 0;
            lock (_syncRoot)
            {
                if (!EnsureSmfHandle())
                    return false;

                byte[] inBuf = new byte[40];
                byte[] guidBytes = interfaceGuid.ToByteArray();
                Buffer.BlockCopy(guidBytes, 0, inBuf, 0, 16);
                BitConverter.GetBytes(channelId).CopyTo(inBuf, 16);
                // cmd = 0 (Read), subcmd = 0 (DataGet) are already 0 in inBuf[20..27]

                byte[] outBuf = new byte[16];
                if (DeviceIoControl(_smfHandle, IOCTL_SMF_CHANNEL_ACTION, inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out uint bytesRet, IntPtr.Zero) && bytesRet >= 8)
                {
                    value = BitConverter.ToInt64(outBuf, 0);
                    return true;
                }

                return false;
            }
        }

        private bool EnsureSshHandle()
        {
            if (IsValidHandle(_sshHandle))
                return true;

            string? resolved = ResolveDeviceInterfacePath(GUID_DEVINTERFACE_SURFACE_SERIAL_HUB, "MSHW0084");
            if (resolved != null)
            {
                _sshHandle = OpenDevice(resolved);
                if (IsValidHandle(_sshHandle))
                    return true;
            }

            _sshHandle = OpenDevice(FallbackSshPath);
            return IsValidHandle(_sshHandle);
        }

        private bool EnsureSmfHandle()
        {
            if (IsValidHandle(_smfHandle))
                return true;

            string? resolved = ResolveDeviceInterfacePath(GUID_DEVINTERFACE_SMF_CORE, "SurfaceSystemManagementFramework");
            if (resolved != null)
            {
                _smfHandle = OpenDevice(resolved);
                if (IsValidHandle(_smfHandle))
                    return true;
            }

            _smfHandle = OpenDevice(FallbackSmfPath);
            return IsValidHandle(_smfHandle);
        }

        private static IntPtr OpenDevice(string path)
        {
            return CreateFileW(
                path,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);
        }

        private static string? ResolveDeviceInterfacePath(Guid interfaceGuid, string requiredSubstring)
        {
            try
            {
                Guid guid = interfaceGuid;
                if (CM_Get_Device_Interface_List_SizeW(out uint len, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) == CR_SUCCESS && len > 1)
                {
                    char[] buf = new char[len];
                    if (CM_Get_Device_Interface_ListW(ref guid, null, buf, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) == CR_SUCCESS)
                    {
                        string multiSz = new string(buf);
                        string[] entries = multiSz.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                        foreach (string entry in entries)
                        {
                            if (entry.Contains(requiredSubstring, StringComparison.OrdinalIgnoreCase))
                            {
                                return entry;
                            }
                        }
                        if (entries.Length > 0)
                        {
                            return entries[0];
                        }
                    }
                }
            }
            catch
            {
                // Ignore and use fallback path
            }
            return null;
        }

        private static bool IsValidHandle(IntPtr handle)
        {
            return handle != IntPtr.Zero && handle != new IntPtr(-1);
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;

                if (IsValidHandle(_sshHandle))
                {
                    CloseHandle(_sshHandle);
                    _sshHandle = IntPtr.Zero;
                }

                if (IsValidHandle(_smfHandle))
                {
                    CloseHandle(_smfHandle);
                    _smfHandle = IntPtr.Zero;
                }
            }
        }
    }
}
