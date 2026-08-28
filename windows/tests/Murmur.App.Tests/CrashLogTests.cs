using Murmur.App;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

public class CrashLogTests
{
    /// <summary>
    /// The path is computed from the settings location, so a change there must not silently
    /// send crash reports somewhere nobody looks.
    /// </summary>
    [Fact]
    public void The_crash_log_sits_beside_the_settings_file()
    {
        var settings = Path.GetDirectoryName(Murmur.Core.AppSettings.DefaultPath);

        Path.GetDirectoryName(Program.CrashLogPath).ShouldBe(settings);
        Path.GetFileName(Program.CrashLogPath).ShouldBe("crash.log");
    }
}
