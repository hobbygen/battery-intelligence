using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryIntelligence.Windows.Interop;

/// <summary>
/// Raw P/Invoke surface for the battery device interface (S4,
/// docs/api-strategy.md section 2): <c>SetupDiGetClassDevs</c> → <c>CreateFile</c>
/// → <c>DeviceIoControl</c>. This is the richest source — cycle count,
/// temperature, manufacture date and the unique ID — used to enrich or repair
/// WinRT/WMI results, never called from above the provider layer.
/// </summary>
internal static partial class BatteryDeviceIoctlInterop
{
    // GUID_DEVICE_INTERFACE_BATTERY (Devguid.h). Fixed, published, never changes.
    internal static readonly Guid DeviceInterfaceBattery = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");

    private const uint DigcfDeviceinterface = 0x00000010;
    private const uint DigcfPresent = 0x00000002;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;
    internal const int InvalidHandleValueCompare = -1;

    // FILE_DEVICE_BATTERY = 0x29; METHOD_BUFFERED = 0; FILE_ANY_ACCESS = 0.
    // CTL_CODE(t, f, m, a) = (t << 16) | (a << 14) | (f << 2) | m
    private const uint FileDeviceBattery = 0x29;
    internal const uint IoctlBatteryQueryTag = (FileDeviceBattery << 16) | (0x10 << 2);
    internal const uint IoctlBatteryQueryInformation = (FileDeviceBattery << 16) | (0x11 << 2);
    internal const uint IoctlBatteryQueryStatus = (FileDeviceBattery << 16) | (0x13 << 2);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [LibraryImport("setupapi.dll", SetLastError = true)]
    internal static partial nint SetupDiGetClassDevsW(
        in Guid classGuid,
        nint enumerator,
        nint hwndParent,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet,
        nint deviceInfoData,
        in Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(
        nint deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        nint deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        nint deviceInfoData);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[]? inBuffer,
        int inBufferSize,
        byte[]? outBuffer,
        int outBufferSize,
        out int bytesReturned,
        nint overlapped);

    /// <summary>
    /// Enumerates the device path for every present battery device interface.
    /// </summary>
    internal static List<string> EnumerateDevicePaths()
    {
        List<string> paths = [];
        nint deviceInfoSet = SetupDiGetClassDevsW(
            DeviceInterfaceBattery,
            0,
            0,
            DigcfDeviceinterface | DigcfPresent);

        if (deviceInfoSet == 0 || deviceInfoSet == InvalidHandleValueCompare)
        {
            return paths;
        }

        try
        {
            uint index = 0;
            while (true)
            {
                SP_DEVICE_INTERFACE_DATA data = default;
                data.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, 0, DeviceInterfaceBattery, index, ref data))
                {
                    break;
                }

                if (SetupDiGetDeviceInterfaceDetailW(deviceInfoSet, ref data, 0, 0, out uint requiredSize, 0)
                    || requiredSize > 0)
                {
                    nint detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // First field of SP_DEVICE_INTERFACE_DETAIL_DATA_W is a DWORD
                        // cbSize; on x64 the struct requires 8-byte alignment, and the
                        // documented required value is (size of the fixed portion),
                        // which the runtime's marshaller reports via cbSize = 6 on x64,
                        // 5 on x86 (4-byte DWORD + wchar_t) — but since this call only
                        // ever reads DevicePath (an inline WCHAR buffer), it is
                        // simplest and robust to just set cbSize to the correct native
                        // struct size for the current pointer width.
                        Marshal.WriteInt32(detailBuffer, nint.Size == 8 ? 8 : 6);

                        if (SetupDiGetDeviceInterfaceDetailW(
                                deviceInfoSet, ref data, detailBuffer, requiredSize, out _, 0))
                        {
                            string path = Marshal.PtrToStringUni(detailBuffer + 4) ?? string.Empty;
                            if (!string.IsNullOrEmpty(path))
                            {
                                paths.Add(path);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }

                index++;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return paths;
    }

    internal static SafeFileHandle? OpenDevice(string devicePath)
    {
        SafeFileHandle handle = CreateFileW(
            devicePath,
            GenericRead | GenericWrite,
            FileShareReadWrite,
            0,
            OpenExisting,
            0,
            0);

        return handle.IsInvalid ? null : handle;
    }
}
