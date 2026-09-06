using System.Buffers.Binary;
using System.Text;
using BatteryIntelligence.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace BatteryIntelligence.Windows;

/// <summary>Which <c>BATTERY_QUERY_INFORMATION_LEVEL</c> to request.</summary>
/// <remarks>Numeric values match the native enum exactly; do not renumber.</remarks>
public enum BatteryInformationLevel
{
    BatteryInformation = 0,
    BatteryGranularityInformation = 1,
    BatteryTemperature = 2,
    BatteryEstimatedTime = 3,
    BatteryDeviceName = 4,
    BatteryManufactureDate = 5,
    BatteryManufactureName = 6,
    BatteryUniqueID = 7,
    BatterySerialNumber = 8,
}

/// <summary><c>BATTERY_INFORMATION.Capabilities</c> flags (quirk Q2 lives here).</summary>
[Flags]
public enum BatteryCapabilityFlags : uint
{
    None = 0,
    SetChargeSupported = 0x00000001,
    SetDischargeSupported = 0x00000002,
    IsShortTerm = 0x20000000,

    /// <summary>Rate and capacity are reported in mA/mAh, not mW/mWh (quirk Q2).</summary>
    CapacityRelative = 0x40000000,
    SystemBattery = 0x80000000,
}

/// <summary><c>BATTERY_STATUS.PowerState</c> flags.</summary>
[Flags]
public enum BatteryPowerStateFlags : uint
{
    None = 0,
    PowerOnLine = 0x00000001,
    Discharging = 0x00000002,
    Charging = 0x00000004,
    Critical = 0x00000008,
}

/// <summary>Decoded <c>BATTERY_INFORMATION</c> (identity/design-time facts).</summary>
public sealed record BatteryStaticInfo(
    BatteryCapabilityFlags Capabilities,
    string Chemistry,
    uint DesignedCapacity,
    uint FullChargedCapacity,
    uint CycleCount);

/// <summary>Decoded <c>BATTERY_STATUS</c> (live electrical state).</summary>
/// <param name="PowerState">Flags: on AC, charging, discharging, critical.</param>
/// <param name="CapacityRemaining">Remaining capacity, mWh (or mAh under quirk Q2).</param>
/// <param name="Voltage">Voltage, mV.</param>
/// <param name="Rate">
/// Signed mW (or mA under quirk Q2): positive charging, negative discharging —
/// this is the native sign convention and matches docs/database.md directly.
/// </param>
public sealed record BatteryLiveStatus(
    BatteryPowerStateFlags PowerState,
    uint CapacityRemaining,
    uint Voltage,
    int Rate);

/// <summary>
/// Opens one battery device interface and issues the S4 IOCTL queries against it.
/// One instance owns one native handle; dispose to release it.
/// </summary>
public sealed class BatteryIoctlDevice : IDisposable
{
    private readonly SafeFileHandle _handle;

    private BatteryIoctlDevice(SafeFileHandle handle, uint tag)
    {
        _handle = handle;
        Tag = tag;
    }

    /// <summary>The battery tag, re-queried on every open — a stale tag fails every subsequent IOCTL (see docs/api-strategy.md section 2).</summary>
    public uint Tag { get; }

    /// <summary>
    /// Opens the device at <paramref name="devicePath"/> and queries its tag.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the device cannot be opened or reports no tag —
    /// both legitimate outcomes (a battery slot with no battery present reports no
    /// tag), never an exception.
    /// </returns>
    public static BatteryIoctlDevice? TryOpen(string devicePath)
    {
        SafeFileHandle? handle = BatteryDeviceIoctlInterop.OpenDevice(devicePath);
        if (handle is null)
        {
            return null;
        }

        byte[] tagInBuffer = new byte[4]; // input: ULONG(0) meaning "any battery at this interface"
        byte[] tagOutBuffer = new byte[4];

        bool ok = BatteryDeviceIoctlInterop.DeviceIoControl(
            handle,
            BatteryDeviceIoctlInterop.IoctlBatteryQueryTag,
            tagInBuffer,
            tagInBuffer.Length,
            tagOutBuffer,
            tagOutBuffer.Length,
            out int returned,
            0);

        if (!ok || returned < 4)
        {
            handle.Dispose();
            return null;
        }

        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(tagOutBuffer);
        return tag == 0 ? null : new BatteryIoctlDevice(handle, tag);
    }

    /// <summary>Queries <c>BatteryInformation</c> — design capacity, chemistry, capability flags, cycle count.</summary>
    public BatteryStaticInfo? QueryStaticInfo()
    {
        byte[]? raw = QueryFixedLevel(BatteryInformationLevel.BatteryInformation, 36);
        if (raw is null)
        {
            return null;
        }

        uint capabilities = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0, 4));
        string chemistry = DecodeChemistryTag(raw.AsSpan(4, 4));
        uint designed = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8, 4));
        uint fullCharged = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12, 4));
        uint cycleCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(32, 4));

        return new BatteryStaticInfo((BatteryCapabilityFlags)capabilities, chemistry, designed, fullCharged, cycleCount);
    }

    /// <summary>Queries live status: power state flags, remaining capacity, voltage, signed rate.</summary>
    public BatteryLiveStatus? QueryLiveStatus()
    {
        byte[] inBuffer = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(inBuffer.AsSpan(0, 4), Tag);
        // Timeout=0, PowerState=0, LowCapacity=0, HighCapacity=0: immediate, unconditional query.

        byte[] outBuffer = new byte[16];
        bool ok = BatteryDeviceIoctlInterop.DeviceIoControl(
            _handle,
            BatteryDeviceIoctlInterop.IoctlBatteryQueryStatus,
            inBuffer,
            inBuffer.Length,
            outBuffer,
            outBuffer.Length,
            out int returned,
            0);

        if (!ok || returned < 16)
        {
            return null;
        }

        uint powerState = BinaryPrimitives.ReadUInt32LittleEndian(outBuffer.AsSpan(0, 4));
        uint capacity = BinaryPrimitives.ReadUInt32LittleEndian(outBuffer.AsSpan(4, 4));
        uint voltage = BinaryPrimitives.ReadUInt32LittleEndian(outBuffer.AsSpan(8, 4));
        int rate = BinaryPrimitives.ReadInt32LittleEndian(outBuffer.AsSpan(12, 4));

        return new BatteryLiveStatus((BatteryPowerStateFlags)powerState, capacity, voltage, rate);
    }

    /// <summary>Queries the deci-Kelvin temperature reading, or <see langword="null"/> if unsupported/absent.</summary>
    public uint? QueryTemperatureDeciKelvin()
    {
        byte[]? raw = QueryFixedLevel(BatteryInformationLevel.BatteryTemperature, 4);
        return raw is null ? null : BinaryPrimitives.ReadUInt32LittleEndian(raw);
    }

    public string? QueryManufactureName() => QueryStringLevel(BatteryInformationLevel.BatteryManufactureName);

    public string? QueryDeviceName() => QueryStringLevel(BatteryInformationLevel.BatteryDeviceName);

    public string? QueryUniqueId() => QueryStringLevel(BatteryInformationLevel.BatteryUniqueID);

    public string? QuerySerialNumber() => QueryStringLevel(BatteryInformationLevel.BatterySerialNumber);

    private byte[]? QueryFixedLevel(BatteryInformationLevel level, int outSize)
    {
        byte[] inBuffer = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(inBuffer.AsSpan(0, 4), Tag);
        BinaryPrimitives.WriteInt32LittleEndian(inBuffer.AsSpan(4, 4), (int)level);
        // AtRate = 0: not applicable to any level queried here.

        byte[] outBuffer = new byte[outSize];
        bool ok = BatteryDeviceIoctlInterop.DeviceIoControl(
            _handle,
            BatteryDeviceIoctlInterop.IoctlBatteryQueryInformation,
            inBuffer,
            inBuffer.Length,
            outBuffer,
            outBuffer.Length,
            out int returned,
            0);

        return ok && returned >= outSize ? outBuffer : null;
    }

    private string? QueryStringLevel(BatteryInformationLevel level)
    {
        // String levels return a null-terminated WCHAR buffer of unpredictable
        // length; 512 bytes (256 UTF-16 chars) comfortably covers every
        // manufacturer string observed in practice.
        const int bufferSize = 512;
        byte[]? raw = QueryFixedLevel(level, bufferSize);
        if (raw is null)
        {
            return null;
        }

        string text = Encoding.Unicode.GetString(raw);
        int nullIndex = text.IndexOf('\0');
        text = nullIndex >= 0 ? text[..nullIndex] : text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string DecodeChemistryTag(ReadOnlySpan<byte> bytes)
    {
        // Packed ASCII tag (e.g. "LiP"), not a null-terminated C string, and
        // usually padded with trailing zero bytes rather than spaces.
        Span<char> chars = stackalloc char[4];
        int length = 0;
        foreach (byte b in bytes)
        {
            if (b == 0)
            {
                break;
            }

            chars[length++] = (char)b;
        }

        return length == 0 ? string.Empty : new string(chars[..length]);
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>Enumerates every present battery device interface path (S4).</summary>
public static class BatteryDeviceEnumerator
{
    public static IReadOnlyList<string> EnumerateDevicePaths() =>
        BatteryDeviceIoctlInterop.EnumerateDevicePaths();
}
