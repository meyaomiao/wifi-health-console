using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WiFiHealthConsole.App.Interop;

internal static class NativeWlan
{
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorAccessDenied = 5;
    internal const uint ErrorNotFound = 1168;
    internal const uint ClientVersionLonghorn = 2;
    internal const int MaxNameLength = 256;
    internal const int MaxSsidLength = 32;
    internal const int RateSetMaximumLength = 126;

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanOpenHandle(
        uint clientVersion,
        IntPtr reserved,
        out uint negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        WlanIntfOpcode opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out WlanOpcodeValueType opcodeValueType);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanScan(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        IntPtr dot11Ssid,
        IntPtr informationElements,
        IntPtr reserved);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern uint WlanGetNetworkBssList(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        IntPtr dot11Ssid,
        Dot11BssType bssType,
        [MarshalAs(UnmanagedType.Bool)] bool securityEnabled,
        IntPtr reserved,
        out IntPtr bssList);

    [DllImport("wlanapi.dll", SetLastError = false)]
    internal static extern void WlanFreeMemory(IntPtr memory);

    internal static void ThrowIfError(uint errorCode, string operation)
    {
        if (errorCode == ErrorSuccess)
        {
            return;
        }

        throw new WlanNativeException(errorCode, operation);
    }
}

internal sealed class WlanNativeException : Win32Exception
{
    internal WlanNativeException(uint nativeErrorCode, string operation)
        : base(unchecked((int)nativeErrorCode), $"{operation} 失败：{new Win32Exception(unchecked((int)nativeErrorCode)).Message}")
    {
        NativeErrorCodeUnsigned = nativeErrorCode;
        Operation = operation;
    }

    internal uint NativeErrorCodeUnsigned { get; }

    internal string Operation { get; }

    internal bool IsAccessDenied => NativeErrorCodeUnsigned == NativeWlan.ErrorAccessDenied;
}

internal enum WlanInterfaceState
{
    NotReady = 0,
    Connected = 1,
    AdHocNetworkFormed = 2,
    Disconnecting = 3,
    Disconnected = 4,
    Associating = 5,
    Discovering = 6,
    Authenticating = 7
}

internal enum WlanIntfOpcode
{
    AutoconfEnabled = 1,
    BackgroundScanEnabled = 2,
    MediaStreamingMode = 3,
    RadioState = 4,
    BssType = 5,
    InterfaceState = 6,
    CurrentConnection = 7,
    ChannelNumber = 8,
    SupportedInfrastructureAuthCipherPairs = 9,
    SupportedAdHocAuthCipherPairs = 10,
    SupportedCountryOrRegionStringList = 11,
    CurrentOperationMode = 12,
    SupportedSafeMode = 13,
    CertifiedSafeMode = 14
}

internal enum WlanOpcodeValueType
{
    QueryOnly = 0,
    SetByGroupPolicy = 1,
    SetByUser = 2,
    Invalid = 3
}

internal enum WlanConnectionMode
{
    Profile = 0,
    TemporaryProfile = 1,
    DiscoverySecure = 2,
    DiscoveryUnsecure = 3,
    Auto = 4,
    Invalid = 5
}

internal enum Dot11BssType
{
    Infrastructure = 1,
    Independent = 2,
    Any = 3
}

internal enum Dot11PhyType : uint
{
    Unknown = 0,
    Any = 0,
    Fhss = 1,
    Dsss = 2,
    IrBaseband = 3,
    Ofdm = 4,
    Hrdsss = 5,
    Erp = 6,
    Ht = 7,
    Vht = 8,
    Dmg = 9,
    He = 10,
    Eht = 11
}

internal enum Dot11AuthAlgorithm : uint
{
    Open = 1,
    SharedKey = 2,
    Wpa = 3,
    WpaPsk = 4,
    WpaNone = 5,
    Rsna = 6,
    RsnaPsk = 7,
    Wpa3 = 8,
    Wpa3Sae = 9,
    Owe = 10,
    Wpa3Enterprise192 = 11,
    Wpa3Enterprise = 12,
    Wpa3EnterprisePsk = 13
}

internal enum Dot11CipherAlgorithm : uint
{
    None = 0x00,
    Wep40 = 0x01,
    Tkip = 0x02,
    Ccmp = 0x04,
    Wep104 = 0x05,
    Bip = 0x06,
    Gcmp = 0x08,
    Gcmp256 = 0x09,
    Ccmp256 = 0x0a,
    BipGmac128 = 0x0b,
    BipGmac256 = 0x0c,
    BipCmac256 = 0x0d,
    Wep = 0x101
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanInterfaceInfo
{
    internal Guid InterfaceGuid;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NativeWlan.MaxNameLength)]
    internal string InterfaceDescription;

    internal WlanInterfaceState State;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Dot11Ssid
{
    internal uint SsidLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeWlan.MaxSsidLength)]
    internal byte[] Ssid;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanConnectionAttributes
{
    internal WlanInterfaceState InterfaceState;
    internal WlanConnectionMode ConnectionMode;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NativeWlan.MaxNameLength)]
    internal string ProfileName;

    internal WlanAssociationAttributes AssociationAttributes;
    internal WlanSecurityAttributes SecurityAttributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanAssociationAttributes
{
    internal Dot11Ssid Ssid;
    internal Dot11BssType BssType;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    internal byte[] Bssid;

    internal Dot11PhyType PhyType;
    internal uint PhyIndex;
    internal uint SignalQuality;
    internal uint ReceiveRateKbps;
    internal uint TransmitRateKbps;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanSecurityAttributes
{
    [MarshalAs(UnmanagedType.Bool)]
    internal bool SecurityEnabled;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool OneXEnabled;

    internal Dot11AuthAlgorithm Authentication;
    internal Dot11CipherAlgorithm Cipher;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanRateSet
{
    internal uint RateSetLength;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeWlan.RateSetMaximumLength)]
    internal ushort[] Rates;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanBssEntry
{
    internal Dot11Ssid Ssid;
    internal uint PhyId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    internal byte[] Bssid;

    internal Dot11BssType BssType;
    internal Dot11PhyType PhyType;
    internal int RssiDbm;
    internal uint LinkQuality;

    [MarshalAs(UnmanagedType.U1)]
    internal bool IsInRegulatoryDomain;

    internal ushort BeaconPeriod;
    internal ulong Timestamp;
    internal ulong HostTimestamp;
    internal ushort CapabilityInformation;
    internal uint CenterFrequencyKHz;
    internal WlanRateSet RateSet;
    internal uint InformationElementOffset;
    internal uint InformationElementSize;
}
