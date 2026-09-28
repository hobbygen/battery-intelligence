using BatteryIntelligence.Core.Alerts;
using BatteryIntelligence.Core.Configuration;
using BatteryIntelligence.Core.Enums;
using Xunit;

namespace BatteryIntelligence.Tests.Unit.Alerts;

public sealed class VoiceAlertCuesTests
{
    [Theory]
    [InlineData(AlertType.FullyCharged, VoiceAlertCue.FullyCharged)]
    [InlineData(AlertType.LowBattery, VoiceAlertCue.NeedsCharger)]
    [InlineData(AlertType.CriticalBattery, VoiceAlertCue.NeedsCharger)]
    public void BatteryLevelAlerts_MapToAVoice(AlertType type, VoiceAlertCue expected) =>
        Assert.Equal(expected, VoiceAlertCues.For(type));

    [Theory]
    [InlineData(AlertType.HighTemperature)]
    [InlineData(AlertType.ChargerConnected)]
    [InlineData(AlertType.HealthDegradation)]
    public void OtherAlerts_HaveNoVoice(AlertType type) => Assert.Null(VoiceAlertCues.For(type));

    [Fact]
    public void CriticalOutranksLow_WhichOutranksFull()
    {
        Assert.True(VoiceAlertCues.Priority(AlertType.CriticalBattery) > VoiceAlertCues.Priority(AlertType.LowBattery));
        Assert.True(VoiceAlertCues.Priority(AlertType.LowBattery) > VoiceAlertCues.Priority(AlertType.FullyCharged));
    }

    [Theory]
    [InlineData("", NotificationSettings.DefaultFullChargeVoice)]
    [InlineData("   ", NotificationSettings.DefaultFullChargeVoice)]
    [InlineData("Full_Batt_14.mp3", "Full_Batt_14.mp3")]
    [InlineData(@"..\..\Windows\evil.wav", "evil.wav")]
    public void Validate_KeepsOnlyABareFileName(string stored, string expected)
    {
        NotificationSettings s = new() { FullChargeVoice = stored, VoiceVolumePercent = 250 };
        s.Validate();
        Assert.Equal(expected, s.FullChargeVoice);
        Assert.Equal(100, s.VoiceVolumePercent);
    }
}
