using System.Runtime.InteropServices;

namespace ChatMixr.Services;

/// <summary>
/// Changes Windows' audio settings the way Windows' own Sound settings do: which device is the
/// default (playback or recording, per role) and what a device is called. Windows has no public
/// interface for either; the undocumented but long-stable IPolicyConfig the Sound control panel uses
/// is the standard way (SoundSwitch and AudioDeviceCmdlets use it too), and it needs no administrator
/// rights. Call it from the mix engine's audio thread.
/// </summary>
internal static class AudioPolicy
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Make the endpoint the default for one role only (Separate chat moves just communications).
    /// Returns false (and logs) on failure.
    /// </summary>
    public static bool SetRole(string endpointId, int role)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            int hr = policy.SetDefaultEndpoint(endpointId, role);
            if (hr != 0)
            {
                Logger.Warn("Making {Endpoint} the default (role {Role}) failed: 0x{Result:X8}", endpointId, role, hr);
            }

            return hr == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>
    /// Rename an endpoint (playback or recording): its own name, the part before the device's name in
    /// brackets, exactly as Rename in Windows' Sound settings does. Returns false (and logs) on failure.
    /// </summary>
    public static bool Rename(string endpointId, string name)
    {
        var key = CoreAudio.PKEY_Device_DeviceDesc;
        IntPtr keyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<CoreAudio.PROPERTYKEY>());
        IntPtr valuePointer = Marshal.AllocHGlobal(Marshal.SizeOf<CoreAudio.PROPVARIANT>());
        IntPtr text = Marshal.StringToCoTaskMemUni(name);
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            Marshal.StructureToPtr(key, keyPointer, false);
            Marshal.StructureToPtr(new CoreAudio.PROPVARIANT { vt = CoreAudio.VT_LPWSTR, p = text }, valuePointer, false);
            int hr = policy.SetPropertyValue(endpointId, 0, keyPointer, valuePointer);
            if (hr != 0)
            {
                Logger.Warn("Renaming {Endpoint} to {Name} failed: 0x{Result:X8}", endpointId, name, hr);
            }

            return hr == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
            Marshal.FreeCoTaskMem(text);
            Marshal.FreeHGlobal(valuePointer);
            Marshal.FreeHGlobal(keyPointer);
        }
    }

    /// <summary>
    /// Show or hide an endpoint everywhere in Windows, as Disable and Enable in Sound settings do. A
    /// hidden endpoint isn't listed by Windows, games or ChatMixr until shown again. Returns false (and
    /// logs) on failure.
    /// </summary>
    public static bool SetVisible(string endpointId, bool visible)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            int hr = policy.SetEndpointVisibility(endpointId, visible ? 1 : 0);
            if (hr != 0)
            {
                Logger.Warn("Setting {Endpoint} visible={Visible} failed: 0x{Result:X8}", endpointId, visible, hr);
            }

            return hr == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>The roles games and media use: console (games and system sounds) and multimedia.</summary>
    private static readonly int[] OutputRoles = [0, 1];

    /// <summary>Every role: console, multimedia and communications.</summary>
    private static readonly int[] AllRoles = [0, 1, 2];

    /// <summary>
    /// Make a playback endpoint the default for games and media, leaving calls (communications) where
    /// they are. Returns false (and logs) on failure.
    /// </summary>
    public static bool SetOutput(string endpointId) => SetRoles(endpointId, OutputRoles);

    /// <summary>Make an endpoint the default for every role (a microphone). Returns false (and logs) on failure.</summary>
    public static bool SetAllRoles(string endpointId) => SetRoles(endpointId, AllRoles);

    private static bool SetRoles(string endpointId, int[] roles)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (int role in roles)
            {
                int hr = policy.SetDefaultEndpoint(endpointId, role);
                if (hr != 0)
                {
                    Logger.Warn("Making {Endpoint} the default (role {Role}) failed: 0x{Result:X8}", endpointId, role, hr);
                    return false;
                }
            }

            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    /// <summary>Method order is the vtable order and must not change; only SetDefaultEndpoint is called.</summary>
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
