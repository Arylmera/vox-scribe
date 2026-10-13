using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views.Chibi;

/// <summary>A selectable mascot.</summary>
/// <param name="Id">Settings id.</param>
/// <param name="Label">Display name.</param>
/// <param name="Create">Builds the face with an accent colour.</param>
internal sealed record Mascot(string Id, string Label, Func<Color, ChibiFace> Create);

/// <summary>The mascot catalogue and the rules for which one, if any, is shown.</summary>
internal static class Mascots
{
    /// <summary>Every mascot, in picker order.</summary>
    public static IReadOnlyList<Mascot> All { get; } =
    [
        new("servo-skull", "Servo-skull", a => new ServoSkullChibi(a)),
        new("netrunner", "Netrunner", a => new NetrunnerChibi(a)),
        new("wizard", "Wizard", a => new WizardChibi(a)),
        new("clawd", "Clawd", a => new ClawdChibi(a)),
        new("owl", "Owl scribe", a => new OwlChibi(a)),
        new("mic", "Vintage mic", a => new MicChibi(a)),
        new("slime", "Slime", a => new SlimeChibi(a)),
    ];

    /// <summary>Every theme's accent variants: the mascot accent override's choices.</summary>
    public static IReadOnlyList<AccentVariant> Accents { get; } = [.. Themes.All.SelectMany(t => t.Variants)];

    /// <summary>The mascot with <paramref name="id"/>, or null.</summary>
    public static Mascot? Find(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The mascot to show instead of the pill, or null for the pill.</summary>
    public static Mascot? Active(SettingsData d) => d.MascotHidden ? null : Find(d.Mascot);

    /// <summary>The pinned accent in the current light/dark mode, or the theme's accent.</summary>
    public static Color Accent(SettingsData d) =>
        Accents.FirstOrDefault(v => string.Equals(v.Id, d.MascotAccent, StringComparison.OrdinalIgnoreCase)) is { } v
            ? Tokens.Colors.Rgb(Themes.IsDark ? v.Dark : v.Light)
            : Tokens.Colors.Accent;
}
