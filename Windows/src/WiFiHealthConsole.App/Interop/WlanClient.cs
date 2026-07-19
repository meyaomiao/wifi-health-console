using System.Runtime.InteropServices;
using System.Text;

namespace WiFiHealthConsole.App.Interop;

internal sealed class WlanClient : IDisposable
{
    private const int CollectionHeaderSize = sizeof(uint) * 2;
    private const int BssListCountOffset = sizeof(uint);
    private const int MaximumReasonableInterfaceCount = 128;
    private const int MaximumReasonableBssCount = 16_384;
    private const uint MaximumReasonableNativeBufferSize = 256 * 1024 * 1024;

    private IntPtr _handle;

    internal WlanClient()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native WLAN API 仅在 Windows 上可用。");
        }

        var result = NativeWlan.WlanOpenHandle(
            NativeWlan.ClientVersionLonghorn,
            IntPtr.Zero,
            out _,
            out _handle);

        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanOpenHandle));
    }

    internal IReadOnlyList<NativeWifiInterface> GetInterfaces()
    {
        ThrowIfDisposed();

        var result = NativeWlan.WlanEnumInterfaces(_handle, IntPtr.Zero, out var listPointer);
        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanEnumInterfaces));

        try
        {
            if (listPointer == IntPtr.Zero)
            {
                return [];
            }

            var count = ReadBoundedCount(listPointer, MaximumReasonableInterfaceCount, "Wi-Fi 接口");
            var itemSize = Marshal.SizeOf<WlanInterfaceInfo>();
            var items = new List<NativeWifiInterface>(count);

            for (var index = 0; index < count; index++)
            {
                var itemPointer = IntPtr.Add(listPointer, checked(CollectionHeaderSize + (index * itemSize)));
                var item = Marshal.PtrToStructure<WlanInterfaceInfo>(itemPointer);
                items.Add(new NativeWifiInterface(
                    item.InterfaceGuid,
                    item.InterfaceDescription?.TrimEnd('\0') ?? string.Empty,
                    item.State));
            }

            return items;
        }
        finally
        {
            FreeNativeMemory(listPointer);
        }
    }

    internal NativeConnection? GetCurrentConnection(NativeWifiInterface wifiInterface)
    {
        ThrowIfDisposed();

        if (wifiInterface.State != WlanInterfaceState.Connected)
        {
            return null;
        }

        var interfaceId = wifiInterface.Id;
        var result = NativeWlan.WlanQueryInterface(
            _handle,
            ref interfaceId,
            WlanIntfOpcode.CurrentConnection,
            IntPtr.Zero,
            out var dataSize,
            out var dataPointer,
            out _);

        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanQueryInterface));

        try
        {
            var requiredSize = Marshal.SizeOf<WlanConnectionAttributes>();
            if (dataPointer == IntPtr.Zero || dataSize < requiredSize)
            {
                throw new InvalidDataException("Native WLAN 返回的当前连接数据不完整。");
            }

            var attributes = Marshal.PtrToStructure<WlanConnectionAttributes>(dataPointer);
            var association = attributes.AssociationAttributes;
            var security = attributes.SecurityAttributes;

            return new NativeConnection(
                wifiInterface,
                DecodeSsid(association.Ssid),
                FormatMacAddress(association.Bssid),
                ClampQuality(association.SignalQuality),
                association.ReceiveRateKbps / 1_000d,
                association.TransmitRateKbps / 1_000d,
                DescribeAuthentication(security.Authentication),
                DescribeCipher(security.Cipher),
                association.PhyType);
        }
        finally
        {
            FreeNativeMemory(dataPointer);
        }
    }

    internal int? GetCurrentChannel(NativeWifiInterface wifiInterface)
    {
        ThrowIfDisposed();
        var interfaceId = wifiInterface.Id;
        var result = NativeWlan.WlanQueryInterface(
            _handle,
            ref interfaceId,
            WlanIntfOpcode.ChannelNumber,
            IntPtr.Zero,
            out var dataSize,
            out var dataPointer,
            out _);

        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanQueryInterface));

        try
        {
            if (dataPointer == IntPtr.Zero || dataSize < sizeof(uint))
            {
                return null;
            }

            var channel = unchecked((uint)Marshal.ReadInt32(dataPointer));
            return channel is > 0 and <= 255 ? checked((int)channel) : null;
        }
        finally
        {
            FreeNativeMemory(dataPointer);
        }
    }

    internal void RequestScan(NativeWifiInterface wifiInterface)
    {
        ThrowIfDisposed();
        var interfaceId = wifiInterface.Id;
        var result = NativeWlan.WlanScan(
            _handle,
            ref interfaceId,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanScan));
    }

    internal IReadOnlyList<NativeBssNetwork> GetBssNetworks(NativeWifiInterface wifiInterface)
    {
        ThrowIfDisposed();
        var interfaceId = wifiInterface.Id;
        var result = NativeWlan.WlanGetNetworkBssList(
            _handle,
            ref interfaceId,
            IntPtr.Zero,
            Dot11BssType.Any,
            securityEnabled: false,
            IntPtr.Zero,
            out var listPointer);

        NativeWlan.ThrowIfError(result, nameof(NativeWlan.WlanGetNetworkBssList));

        try
        {
            if (listPointer == IntPtr.Zero)
            {
                return [];
            }

            var totalSize = unchecked((uint)Marshal.ReadInt32(listPointer, 0));
            if (totalSize < CollectionHeaderSize || totalSize > MaximumReasonableNativeBufferSize)
            {
                throw new InvalidDataException($"Native WLAN BSS 列表长度异常：{totalSize} 字节。");
            }

            // WLAN_BSS_LIST starts with dwTotalSize followed by dwNumberOfItems.
            var count = ReadBoundedCount(
                listPointer,
                MaximumReasonableBssCount,
                "BSS",
                BssListCountOffset);
            var entrySize = Marshal.SizeOf<WlanBssEntry>();
            var fixedEntriesSize = checked(CollectionHeaderSize + (count * entrySize));
            if ((uint)fixedEntriesSize > totalSize)
            {
                throw new InvalidDataException("Native WLAN BSS 列表的固定区域越界。");
            }

            var networks = new List<NativeBssNetwork>(count);
            for (var index = 0; index < count; index++)
            {
                var entryOffset = checked(CollectionHeaderSize + (index * entrySize));
                var entryPointer = IntPtr.Add(listPointer, entryOffset);
                var entry = Marshal.PtrToStructure<WlanBssEntry>(entryPointer);
                var informationElements = CopyInformationElements(
                    listPointer,
                    totalSize,
                    entryPointer,
                    entry.InformationElementOffset,
                    entry.InformationElementSize);
                var parsed = WifiInformationElements.Parse(informationElements);
                var centerFrequencyMHz = entry.CenterFrequencyKHz > 0
                    ? checked((int)(entry.CenterFrequencyKHz / 1_000))
                    : (int?)null;
                var frequencyChannel = centerFrequencyMHz is { } frequency
                    ? WifiChannelMath.ChannelFromFrequencyMHz(frequency)
                    : null;

                networks.Add(new NativeBssNetwork(
                    wifiInterface,
                    DecodeSsid(entry.Ssid),
                    FormatMacAddress(entry.Bssid),
                    entry.RssiDbm,
                    ClampQuality(entry.LinkQuality),
                    centerFrequencyMHz,
                    parsed.PrimaryChannel ?? frequencyChannel,
                    parsed.ChannelWidthMHz,
                    parsed.Authentication,
                    parsed.Cipher,
                    entry.PhyType,
                    informationElements.Length > 0));
            }

            return networks;
        }
        finally
        {
            FreeNativeMemory(listPointer);
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            _ = NativeWlan.WlanCloseHandle(handle, IntPtr.Zero);
        }

        GC.SuppressFinalize(this);
    }

    internal static int ReadBoundedCount(
        IntPtr listPointer,
        int maximum,
        string collectionName,
        int countOffset = 0)
    {
        var rawCount = unchecked((uint)Marshal.ReadInt32(listPointer, countOffset));
        if (rawCount > maximum)
        {
            throw new InvalidDataException($"Native WLAN {collectionName} 数量异常：{rawCount}。");
        }

        return checked((int)rawCount);
    }

    private static byte[] CopyInformationElements(
        IntPtr listPointer,
        uint totalSize,
        IntPtr entryPointer,
        uint informationElementOffset,
        uint informationElementSize)
    {
        if (informationElementSize == 0)
        {
            return [];
        }

        var listStart = listPointer.ToInt64();
        var listEnd = checked(listStart + totalSize);
        var informationStart = checked(entryPointer.ToInt64() + informationElementOffset);
        var informationEnd = checked(informationStart + informationElementSize);

        if (informationStart < listStart || informationEnd > listEnd || informationEnd < informationStart)
        {
            return [];
        }

        var length = checked((int)informationElementSize);
        var bytes = new byte[length];
        Marshal.Copy(new IntPtr(informationStart), bytes, 0, length);
        return bytes;
    }

    private static string DecodeSsid(Dot11Ssid nativeSsid)
    {
        if (nativeSsid.Ssid is null || nativeSsid.SsidLength == 0)
        {
            return string.Empty;
        }

        var length = Math.Min(checked((int)nativeSsid.SsidLength), nativeSsid.Ssid.Length);
        return Encoding.UTF8.GetString(nativeSsid.Ssid, 0, length);
    }

    private static string FormatMacAddress(byte[]? address)
    {
        if (address is null || address.Length < 6)
        {
            return string.Empty;
        }

        return string.Join(":", address.Take(6).Select(value => value.ToString("X2")));
    }

    private static uint ClampQuality(uint quality) => Math.Min(quality, 100u);

    private static string DescribeAuthentication(Dot11AuthAlgorithm authentication) => authentication switch
    {
        Dot11AuthAlgorithm.Open => "开放",
        Dot11AuthAlgorithm.SharedKey => "WEP 共享密钥",
        Dot11AuthAlgorithm.Wpa => "WPA-Enterprise",
        Dot11AuthAlgorithm.WpaPsk => "WPA-Personal",
        Dot11AuthAlgorithm.Rsna => "WPA2-Enterprise",
        Dot11AuthAlgorithm.RsnaPsk => "WPA2-Personal",
        Dot11AuthAlgorithm.Wpa3 or Dot11AuthAlgorithm.Wpa3Enterprise => "WPA3-Enterprise",
        Dot11AuthAlgorithm.Wpa3Sae => "WPA3-SAE",
        Dot11AuthAlgorithm.Owe => "OWE",
        Dot11AuthAlgorithm.Wpa3Enterprise192 => "WPA3-Enterprise 192-bit",
        Dot11AuthAlgorithm.Wpa3EnterprisePsk => "WPA3-Enterprise PSK",
        _ => authentication.ToString()
    };

    private static string DescribeCipher(Dot11CipherAlgorithm cipher) => cipher switch
    {
        Dot11CipherAlgorithm.None => "无",
        Dot11CipherAlgorithm.Wep40 => "WEP-40",
        Dot11CipherAlgorithm.Wep104 => "WEP-104",
        Dot11CipherAlgorithm.Wep => "WEP",
        Dot11CipherAlgorithm.Tkip => "TKIP",
        Dot11CipherAlgorithm.Ccmp => "CCMP/AES",
        Dot11CipherAlgorithm.Ccmp256 => "CCMP-256",
        Dot11CipherAlgorithm.Gcmp => "GCMP",
        Dot11CipherAlgorithm.Gcmp256 => "GCMP-256",
        Dot11CipherAlgorithm.Bip => "BIP",
        Dot11CipherAlgorithm.BipGmac128 => "BIP-GMAC-128",
        Dot11CipherAlgorithm.BipGmac256 => "BIP-GMAC-256",
        Dot11CipherAlgorithm.BipCmac256 => "BIP-CMAC-256",
        _ => cipher.ToString()
    };

    private static void FreeNativeMemory(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            NativeWlan.WlanFreeMemory(pointer);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
    }
}

internal sealed record NativeWifiInterface(Guid Id, string Name, WlanInterfaceState State);

internal sealed record NativeConnection(
    NativeWifiInterface Interface,
    string Ssid,
    string Bssid,
    uint SignalQualityPercent,
    double ReceiveRateMbps,
    double TransmitRateMbps,
    string Authentication,
    string Cipher,
    Dot11PhyType PhyType);

internal sealed record NativeBssNetwork(
    NativeWifiInterface Interface,
    string Ssid,
    string Bssid,
    int RssiDbm,
    uint SignalQualityPercent,
    int? CenterFrequencyMHz,
    int? PrimaryChannel,
    int? ChannelWidthMHz,
    string? Authentication,
    string? Cipher,
    Dot11PhyType PhyType,
    bool HasInformationElements);
