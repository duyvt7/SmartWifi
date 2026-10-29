using Company.Core.Abstractions.Enums;
using Company.Core.Abstractions.Models;
using Company.Module.ConfigManager.Services;
using Xunit;

namespace CraneWifi.Tests;

public class CoreTests
{
    [Fact]
    public async Task ConfigService_CreatesDefaultConfig_WhenFileNotExists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.json");
        try
        {
            var configService = new ConfigService(tempFile);
            var config = await configService.LoadAsync();

            Assert.NotNull(config);
            Assert.True(File.Exists(tempFile));
            Assert.True(config.SystemSettings.DisableAirplaneModeOnStartup);
            Assert.True(config.SystemSettings.DisableTabletModeOnStartup);
            Assert.Equal(45, config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset);
            Assert.True(config.SmartWifiSettings.EnableSmartRoaming);
            Assert.NotEmpty(config.WatchdogSettings.TargetServers);
            Assert.NotEmpty(config.SmartWifiSettings.PreferredNetworks);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConfigService_FiresConfigurationChanged_OnSave()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid()}.json");
        try
        {
            var configService = new ConfigService(tempFile);
            await configService.LoadAsync();

            bool eventFired = false;
            configService.ConfigurationChanged += (s, cfg) =>
            {
                eventFired = true;
            };

            var config = configService.CurrentConfig;
            config.WatchdogSettings.MaxDisconnectedSecondsBeforeReset = 60;
            await configService.SaveAsync(config);

            Assert.True(eventFired);
            Assert.Equal(60, configService.CurrentConfig.WatchdogSettings.MaxDisconnectedSecondsBeforeReset);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RollingLoggerService_LogsAndEmitsEvent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_logs_{Guid.NewGuid()}");
        try
        {
            var logger = new RollingFileLoggerService(tempDir);
            LogEntry? emitted = null;
            logger.LogEmitted += (s, e) => emitted = e;

            logger.Log(AppLogLevel.Info, "TestUnit", "Test log message");

            var recent = logger.GetRecentLogs(10);
            Assert.NotEmpty(recent);
            Assert.Equal("Test log message", recent.Last().Message);
            Assert.NotNull(emitted);
            Assert.Equal("TestUnit", emitted.Source);

            var todayFile = Path.Combine(tempDir, $"crane-wifi-{DateTime.Today:yyyyMMdd}.log");
            Assert.True(File.Exists(todayFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("00:1A:2B:3C:4D:5E", true)]
    [InlineData("00-1A-2B-3C-4D-5E", true)]
    [InlineData("invalid-mac", false)]
    [InlineData("", false)]
    public void MacParsing_ValidatesCorrectly(string macStr, bool expectedValid)
    {
        // Simple MAC parser validation
        var parts = macStr.Split(new[] { ':', '-' }, StringSplitOptions.RemoveEmptyEntries);
        bool isValid = parts.Length == 6 && parts.All(p => byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out _));

        Assert.Equal(expectedValid, isValid);
    }
}

