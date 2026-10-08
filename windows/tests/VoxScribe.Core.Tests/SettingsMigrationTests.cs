using Shouldly;
using VoxScribe.Core;
using Xunit;

namespace VoxScribe.CoreTests;

/// <summary>Settings files from before the five themes keep loading.</summary>
public sealed class SettingsMigrationTests
{
    [Fact]
    public void A_void_glass_era_settings_file_still_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Theme": "deep-field", "AccentColor": "#4FD8E8", "KeepHistory": false }""");
        try
        {
            var data = new AppSettings(path).Data;

            data.KeepHistory.ShouldBeFalse("the rest of the file must survive the retired keys");
            data.Theme.ShouldBe("deep-field", "the raw id is kept; the app resolves unknown ids to Paper");
            data.AccentVariant.ShouldBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_fresh_install_is_paper_with_its_first_variant() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.Theme.ShouldBe("paper"),
            d => d.AccentVariant.ShouldBeNull());
}
