using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ChatMixr.Services;

/// <summary>
/// The Windows HID and device-setup calls ChatMixr needs to find receivers and read their reports:
/// list every HID interface, read its path, the container it belongs to (the physical receiver, shared
/// with its audio endpoints) and its report sizes, and open it for shared reading.
/// </summary>
internal static class HidInterop
{
    /// <summary>One HID interface that is present now.</summary>
    /// <param name="Path">The interface path to open.</param>
    /// <param name="ContainerId">The physical device it belongs to, or null if Windows did not say.</param>
    public sealed record HidInterface(string Path, Guid? ContainerId);

    /// <summary>The report layout of an opened HID interface.</summary>
    /// <param name="UsagePage">The top-level collection's usage page (0xFF00 for the ChatMix interface).</param>
    /// <param name="InputReportLength">Bytes per input report, including the report ID.</param>
    /// <param name="OutputReportLength">Bytes per output report, including the report ID; 0 when it takes none.</param>
    public sealed record HidCaps(ushort UsagePage, int InputReportLength, int OutputReportLength);

    /// <summary>
    /// Every HID interface present now whose path contains <paramref name="pathFilter"/> (compared
    /// without case), with its container id.
    /// </summary>
    public static List<HidInterface> ListInterfaces(string pathFilter)
    {
        var result = new List<HidInterface>();
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE_VALUE)
        {
            return result;
        }

        try
        {
            for (int index = 0; ; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data))
                {
                    break;
                }

                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                string? path = InterfacePath(set, ref data, ref info);
                if (path == null || !path.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new HidInterface(path, ContainerIdOf(set, ref info)));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    /// <summary>
    /// Open an interface for reading, shared with every other program reading it (SteelSeries GG
    /// included). Returns an invalid handle on failure; the caller checks <c>IsInvalid</c>.
    /// </summary>
    public static SafeFileHandle OpenForReading(string path) =>
        CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>
    /// Open an interface for sending requests (the status request), on a handle of its own so a write
    /// never waits behind the reader's pending read. Invalid on failure; the caller checks.
    /// </summary>
    public static SafeFileHandle OpenForWriting(string path) =>
        CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>
    /// Cancel every read waiting on the handle, from any thread, so a reader blocked on it returns. Safe
    /// on a handle that is already closed or has nothing pending.
    /// </summary>
    public static void CancelPendingReads(SafeFileHandle handle)
    {
        if (!handle.IsInvalid && !handle.IsClosed)
        {
            CancelIoEx(handle, IntPtr.Zero);
        }
    }

    /// <summary>The report layout of an opened interface, or null if Windows can't describe it.</summary>
    public static HidCaps? CapsOf(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
        {
            return null;
        }

        try
        {
            return HidP_GetCaps(preparsed, out HIDP_CAPS caps) == HIDP_STATUS_SUCCESS
                ? new HidCaps(caps.UsagePage, caps.InputReportByteLength, caps.OutputReportByteLength)
                : null;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>The interface's path, filling <paramref name="info"/> with its device for property reads.</summary>
    private static string? InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, ref SP_DEVINFO_DATA info)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // cbSize is the fixed part's size: a DWORD plus one WCHAR, padded to 8 on 64-bit.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, ref info))
            {
                return null;
            }

            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The device's container id: the same for every interface of one physical receiver.</summary>
    private static Guid? ContainerIdOf(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var key = DEVPKEY_Device_ContainerId;
        byte[] buffer = new byte[16];
        if (!SetupDiGetDeviceProperty(set, ref info, ref key, out uint type, buffer, buffer.Length, out _, 0) || type != DEVPROP_TYPE_GUID)
        {
            return null;
        }

        return new Guid(buffer);
    }

    // ── Native declarations ─────────────────────────────────────────────
    private const int DIGCF_PRESENT = 0x2;
    private const int DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint DEVPROP_TYPE_GUID = 0x0D;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    private static readonly DEVPROPKEY DEVPKEY_Device_ContainerId =
        new() { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 };

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid, int index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint type, byte[] buffer, int size, out int required, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
