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

    [Fact]
    public void Auto_stop_defaults_to_three_seconds() =>
        new SettingsData().AutoStopSilenceSeconds.ShouldBe(3);

    [Fact]
    public void Edit_is_unbound_and_uses_the_cleanup_model_by_default() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.EditKeys.ShouldBeNull(),
            d => d.EditModel.ShouldBeNull());

    /// <summary>Off is 0, not the default — it must be written out, or a restart turns it back on.</summary>
    [Fact]
    public void Auto_stop_off_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            settings.Update(settings.Data with { AutoStopSilenceSeconds = 0 });

            new AppSettings(path).Data.AutoStopSilenceSeconds.ShouldBe(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_mascot_is_off_by_default() =>
        new SettingsData().ShouldSatisfyAllConditions(
            d => d.Mascot.ShouldBeNull(),
            d => d.MascotHidden.ShouldBeFalse(),
            d => d.MascotAccent.ShouldBeNull(),
            d => d.MascotX.ShouldBeNull(),
            d => d.MascotY.ShouldBeNull());

    [Fact]
    public void Mascot_settings_survive_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            settings.Update(settings.Data with
            {
                Mascot = "slime", MascotHidden = true, MascotAccent = "moss", MascotX = 120, MascotY = 900,
            });

            new AppSettings(path).Data.ShouldSatisfyAllConditions(
                d => d.Mascot.ShouldBe("slime"),
                d => d.MascotHidden.ShouldBeTrue(),
                d => d.MascotAccent.ShouldBe("moss"),
                d => d.MascotX.ShouldBe(120),
                d => d.MascotY.ShouldBe(900));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
