namespace WiFiHealthConsole.App.Interop;

internal static class WifiInformationElements
{
    private const byte DsParameterSetElementId = 3;
    private const byte RsnElementId = 48;
    private const byte HtOperationElementId = 61;
    private const byte VhtOperationElementId = 192;
    private const byte VendorSpecificElementId = 221;
    private const byte ExtensionElementId = 255;
    private const byte HeOperationExtensionId = 36;

    internal static ParsedWifiInformation Parse(ReadOnlySpan<byte> informationElements)
    {
        int? primaryChannel = null;
        int? htWidth = null;
        int? vhtWidth = null;
        int? heWidth = null;
        string? authentication = null;
        string? cipher = null;

        var offset = 0;
        while (offset + 2 <= informationElements.Length)
        {
            var elementId = informationElements[offset];
            var elementLength = informationElements[offset + 1];
            var payloadOffset = offset + 2;
            var nextOffset = payloadOffset + elementLength;
            if (nextOffset > informationElements.Length)
            {
                break;
            }

            var payload = informationElements.Slice(payloadOffset, elementLength);
            switch (elementId)
            {
                case DsParameterSetElementId when payload.Length >= 1:
                    primaryChannel ??= ValidChannel(payload[0]);
                    break;

                case HtOperationElementId when payload.Length >= 2:
                    primaryChannel ??= ValidChannel(payload[0]);
                    htWidth = WidthFromHtOperation(payload[1]);
                    break;

                case VhtOperationElementId when payload.Length >= 3:
                    vhtWidth = WidthFromVhtOperation(payload[0], payload[1], payload[2], htWidth);
                    break;

                case ExtensionElementId when payload.Length >= 1 && payload[0] == HeOperationExtensionId:
                    var heOperation = ParseHeOperation(payload);
                    primaryChannel ??= heOperation.PrimaryChannel;
                    heWidth = heOperation.ChannelWidthMHz;
                    break;

                case RsnElementId:
                    var rsn = ParseRsn(payload, isWpaVendorElement: false);
                    authentication ??= rsn.Authentication;
                    cipher ??= rsn.Cipher;
                    break;

                case VendorSpecificElementId when IsWpaVendorElement(payload):
                    var wpa = ParseRsn(payload[4..], isWpaVendorElement: true);
                    authentication ??= wpa.Authentication;
                    cipher ??= wpa.Cipher;
                    break;
            }

            offset = nextOffset;
        }

        return new ParsedWifiInformation(
            primaryChannel,
            heWidth ?? vhtWidth ?? htWidth,
            authentication,
            cipher);
    }

    private static int WidthFromHtOperation(byte htInformationSubset1)
    {
        var secondaryChannelOffset = htInformationSubset1 & 0b11;
        return secondaryChannelOffset is 1 or 3 ? 40 : 20;
    }

    private static int? WidthFromVhtOperation(byte channelWidth, byte centerSegment0, byte centerSegment1, int? htWidth)
    {
        return channelWidth switch
        {
            0 => htWidth ?? 20,
            1 when centerSegment1 != 0 && Math.Abs(centerSegment1 - centerSegment0) == 8 => 160,
            1 => 80,
            2 => 160,
            // 80+80 MHz 不是连续的 160 MHz，MVP 不伪造成单个频宽。
            3 => null,
            _ => null
        };
    }

    private static ParsedHeOperation ParseHeOperation(ReadOnlySpan<byte> payload)
    {
        // payload[0] 是 Element ID Extension。HE Operation 固定部分：
        // HE Operation Parameters(3) + BSS Color(1) + Basic HE-MCS/NSS(2)。
        if (payload.Length < 7)
        {
            return default;
        }

        var operationParameters = payload[1]
            | (payload[2] << 8)
            | (payload[3] << 16);
        var cursor = 7;

        const int vhtOperationInformationPresent = 1 << 14;
        const int coHostedBssPresent = 1 << 15;
        const int sixGhzOperationInformationPresent = 1 << 17;

        if ((operationParameters & vhtOperationInformationPresent) != 0)
        {
            cursor += 3;
        }

        if ((operationParameters & coHostedBssPresent) != 0)
        {
            cursor += 1;
        }

        if ((operationParameters & sixGhzOperationInformationPresent) == 0 || cursor + 5 > payload.Length)
        {
            return default;
        }

        var primaryChannel = ValidChannel(payload[cursor]);
        var control = payload[cursor + 1];
        var channelWidthCode = control & 0b11;
        var centerSegment0 = payload[cursor + 2];
        var centerSegment1 = payload[cursor + 3];
        int? width = channelWidthCode switch
        {
            0 => 20,
            1 => 40,
            2 => 80,
            3 when centerSegment1 != 0 && Math.Abs(centerSegment1 - centerSegment0) == 8 => 160,
            // code 3 也可以表示 80+80；无法安全区分时留空。
            _ => null
        };

        return new ParsedHeOperation(primaryChannel, width);
    }

    private static bool IsWpaVendorElement(ReadOnlySpan<byte> payload) =>
        payload.Length >= 4
        && payload[0] == 0x00
        && payload[1] == 0x50
        && payload[2] == 0xF2
        && payload[3] == 0x01;

    private static ParsedSecurity ParseRsn(ReadOnlySpan<byte> payload, bool isWpaVendorElement)
    {
        // Version(2), Group Cipher(4), Pairwise Count(2), Pairwise Suites,
        // AKM Count(2), AKM Suites. 任何截断都立即返回未知。
        var cursor = 0;
        if (!TryReadUInt16(payload, ref cursor, out var version) || version != 1)
        {
            return default;
        }

        if (!TryReadSuite(payload, ref cursor, out var groupCipher))
        {
            return default;
        }

        if (!TryReadUInt16(payload, ref cursor, out var pairwiseCount) || pairwiseCount > 64)
        {
            return default;
        }

        var pairwise = new List<string>();
        for (var index = 0; index < pairwiseCount; index++)
        {
            if (!TryReadSuite(payload, ref cursor, out var suite))
            {
                return default;
            }

            var described = DescribeCipherSuite(suite);
            if (described is not null && !pairwise.Contains(described, StringComparer.Ordinal))
            {
                pairwise.Add(described);
            }
        }

        if (!TryReadUInt16(payload, ref cursor, out var authenticationCount) || authenticationCount > 64)
        {
            return new ParsedSecurity(
                isWpaVendorElement ? "WPA" : "WPA2/3",
                pairwise.FirstOrDefault() ?? DescribeCipherSuite(groupCipher));
        }

        var authentications = new List<string>();
        for (var index = 0; index < authenticationCount; index++)
        {
            if (!TryReadSuite(payload, ref cursor, out var suite))
            {
                return default;
            }

            var described = DescribeAuthenticationSuite(suite, isWpaVendorElement);
            if (described is not null && !authentications.Contains(described, StringComparer.Ordinal))
            {
                authentications.Add(described);
            }
        }

        return new ParsedSecurity(
            authentications.Count > 0
                ? string.Join(" / ", authentications)
                : isWpaVendorElement ? "WPA" : "WPA2/3",
            pairwise.Count > 0
                ? string.Join(" / ", pairwise)
                : DescribeCipherSuite(groupCipher));
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> payload, ref int cursor, out ushort value)
    {
        if (cursor + 2 > payload.Length)
        {
            value = 0;
            return false;
        }

        value = (ushort)(payload[cursor] | (payload[cursor + 1] << 8));
        cursor += 2;
        return true;
    }

    private static bool TryReadSuite(ReadOnlySpan<byte> payload, ref int cursor, out SecuritySuite suite)
    {
        if (cursor + 4 > payload.Length)
        {
            suite = default;
            return false;
        }

        suite = new SecuritySuite(payload[cursor], payload[cursor + 1], payload[cursor + 2], payload[cursor + 3]);
        cursor += 4;
        return true;
    }

    private static string? DescribeCipherSuite(SecuritySuite suite)
    {
        if (!suite.IsRsnOui && !suite.IsMicrosoftOui)
        {
            return null;
        }

        return suite.Type switch
        {
            0 => "使用组密钥",
            1 => "WEP-40",
            2 => "TKIP",
            4 => "CCMP/AES",
            5 => "WEP-104",
            8 => "GCMP",
            9 => "GCMP-256",
            10 => "CCMP-256",
            _ => null
        };
    }

    private static string? DescribeAuthenticationSuite(SecuritySuite suite, bool isWpaVendorElement)
    {
        if (!suite.IsRsnOui && !suite.IsMicrosoftOui)
        {
            return null;
        }

        if (isWpaVendorElement)
        {
            return suite.Type switch
            {
                1 => "WPA-Enterprise",
                2 => "WPA-Personal",
                _ => "WPA"
            };
        }

        return suite.Type switch
        {
            1 => "WPA2-Enterprise",
            2 => "WPA2-Personal",
            3 => "FT-Enterprise",
            4 => "FT-Personal",
            8 => "WPA3-SAE",
            9 => "FT-SAE",
            11 => "WPA3-Enterprise 192-bit",
            12 => "FT-WPA3-Enterprise 192-bit",
            18 => "OWE",
            _ => "WPA2/3"
        };
    }

    private static int? ValidChannel(byte channel) => channel == 0 ? null : channel;

    private readonly record struct ParsedHeOperation(int? PrimaryChannel, int? ChannelWidthMHz);

    private readonly record struct ParsedSecurity(string? Authentication, string? Cipher);

    private readonly record struct SecuritySuite(byte Oui0, byte Oui1, byte Oui2, byte Type)
    {
        internal bool IsRsnOui => Oui0 == 0x00 && Oui1 == 0x0F && Oui2 == 0xAC;

        internal bool IsMicrosoftOui => Oui0 == 0x00 && Oui1 == 0x50 && Oui2 == 0xF2;
    }
}

internal readonly record struct ParsedWifiInformation(
    int? PrimaryChannel,
    int? ChannelWidthMHz,
    string? Authentication,
    string? Cipher);

internal static class WifiChannelMath
{
    internal static int? ChannelFromFrequencyMHz(int frequencyMHz)
    {
        if (frequencyMHz == 2_484)
        {
            return 14;
        }

        if (frequencyMHz is >= 2_412 and <= 2_472 && (frequencyMHz - 2_407) % 5 == 0)
        {
            return (frequencyMHz - 2_407) / 5;
        }

        if (frequencyMHz == 5_935)
        {
            return 2;
        }

        if (frequencyMHz is >= 5_955 and <= 7_115 && (frequencyMHz - 5_950) % 5 == 0)
        {
            return (frequencyMHz - 5_950) / 5;
        }

        if (frequencyMHz is >= 5_000 and < 5_955 && (frequencyMHz - 5_000) % 5 == 0)
        {
            return (frequencyMHz - 5_000) / 5;
        }

        return null;
    }

    internal static NativeWifiBand BandFromFrequencyMHz(int? frequencyMHz)
    {
        return frequencyMHz switch
        {
            >= 2_400 and < 2_500 => NativeWifiBand.Band2_4GHz,
            >= 5_925 and <= 7_125 => NativeWifiBand.Band6GHz,
            >= 4_900 and < 5_925 => NativeWifiBand.Band5GHz,
            _ => NativeWifiBand.Unknown
        };
    }
}

internal enum NativeWifiBand
{
    Unknown,
    Band2_4GHz,
    Band5GHz,
    Band6GHz
}
