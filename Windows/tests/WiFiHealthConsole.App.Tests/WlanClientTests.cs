using System.Runtime.InteropServices;
using WiFiHealthConsole.App.Interop;

namespace WiFiHealthConsole.App.Tests;

public sealed class WlanClientTests
{
    [Fact]
    public void ReadBoundedCountUsesBssItemCountInsteadOfTotalSize()
    {
        var header = Marshal.AllocHGlobal(sizeof(uint) * 2);
        try
        {
            Marshal.WriteInt32(header, 0, 720);
            Marshal.WriteInt32(header, sizeof(uint), 2);

            var count = WlanClient.ReadBoundedCount(
                header,
                maximum: 16_384,
                collectionName: "BSS",
                countOffset: sizeof(uint));

            Assert.Equal(2, count);
        }
        finally
        {
            Marshal.FreeHGlobal(header);
        }
    }
}
