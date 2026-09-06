using BatteryIntelligence.Core.Battery;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Battery;

public sealed class PercentageJumpDetectorTests
{
    [Theory]
    [InlineData(40.0, 90.0, true)]
    [InlineData(40.0, 55.0, false)]
    [InlineData(40.0, 41.0, false)]
    [InlineData(90.0, 40.0, true)]
    public void IsSuspiciousJump_UsesAbsoluteDifference(double previous, double current, bool expected) =>
        Assert.Equal(expected, PercentageJumpDetector.IsSuspiciousJump(previous, current));

    [Fact]
    public void IsSuspiciousJump_EitherValueMissing_IsFalse()
    {
        Assert.False(PercentageJumpDetector.IsSuspiciousJump(null, 90.0));
        Assert.False(PercentageJumpDetector.IsSuspiciousJump(40.0, null));
        Assert.False(PercentageJumpDetector.IsSuspiciousJump(null, null));
    }

    [Fact]
    public void IsSuspiciousJump_ExactlyAtThreshold_IsNotSuspicious()
    {
        // Strictly greater-than: exactly the threshold is still plausible.
        Assert.False(PercentageJumpDetector.IsSuspiciousJump(40.0, 65.0, thresholdPercent: 25.0));
    }
}
