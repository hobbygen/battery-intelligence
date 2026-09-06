using BatteryIntelligence.Core.Battery;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Battery;

/// <summary>
/// Every sentinel observed in practice on this project's reference machine
/// (docs/capability-matrix.md quirk Q3) must be rejected, per docs/testing.md
/// section 3: "every sentinel ⇒ Unavailable".
/// </summary>
public sealed class BatterySentinelsTests
{
    [Theory]
    [InlineData(0xFFFFFFFFu, true)]
    [InlineData(0x80000000u, true)]
    [InlineData(0u, false)]
    [InlineData(38_008u, false)]
    public void IsSentinel_UInt32(uint value, bool expected) =>
        Assert.Equal(expected, BatterySentinels.IsSentinel(value));

    [Theory]
    [InlineData((ushort)0xFFFF, true)]
    [InlineData((ushort)0, false)]
    [InlineData((ushort)1234, false)]
    public void IsSentinel_UInt16(ushort value, bool expected) =>
        Assert.Equal(expected, BatterySentinels.IsSentinel(value));

    [Theory]
    [InlineData(int.MinValue, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(95_008, false)]
    public void IsSentinel_Int32(int value, bool expected) =>
        Assert.Equal(expected, BatterySentinels.IsSentinel(value));

    [Fact]
    public void IsSentinel_Int64_KnownSentinel()
    {
        // Win32_Battery.EstimatedRunTime returns 71,582,788 minutes on the
        // reference machine: 0xFFFFFFFF reinterpreted (docs/capability-matrix.md
        // quirk Q3), not a 136-year runtime.
        Assert.True(BatterySentinels.IsSentinel(0xFFFFFFFFL));
        Assert.False(BatterySentinels.IsSentinel(71_582_788L));
    }
}
