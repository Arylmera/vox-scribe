using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// The active theme. <see cref="Apply"/> resolves a theme, a variant and the light/dark mode
/// into <see cref="Tokens"/> and raises <see cref="Changed"/>; windows rebuild on that event.
/// </summary>
/// <remarks>
/// A pure function of its three arguments: the app passes Windows' mode in, tests pass
/// <c>dark: true</c> directly. Red is not a theme value — <see cref="Tokens.Colors.Record"/>
/// is the same in every theme and nothing here can touch it.
/// </remarks>
internal static class Themes
{
    /// <summary>Settings id of the default theme.</summary>
    public const string DefaultId = "paper";

    private const byte TintAlphaLight = 0x1A;
    private const byte TintAlphaDark = 0x1F;
    private const uint PositiveLight = 0x107C10;
    private const uint PositiveDark = 0x6CCB5F;
    private const uint CautionLight = 0x8A5200;
    private const uint CautionDark = 0xFFB13D;

    private static bool _applied;

    /// <summary>The selectable themes, in picker order.</summary>
    public static IReadOnlyList<ThemeDefinition> All => ThemeCatalog.All;

    /// <summary>The theme currently painted.</summary>
    public static ThemeDefinition Active { get; private set; } = ThemeCatalog.All[0];

    /// <summary>The accent variant currently painted.</summary>
    public static AccentVariant ActiveVariant { get; private set; } = ThemeCatalog.All[0].Variants[0];

    /// <summary>Whether the dark palette is painted.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after <see cref="Apply"/> changed anything. May fire on any thread that calls Apply.</summary>
    public static event EventHandler? Changed;

    /// <summary>The theme with <paramref name="id"/>, or the default for an unknown or retired id.</summary>
    public static ThemeDefinition Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>
    /// Paints <paramref name="themeId"/> / <paramref name="variantId"/> in the light or dark
    /// palette. Returns false, and raises nothing, when that is already what is painted.
    /// </summary>
    public static bool Apply(string? themeId, string? variantId, bool dark)
    {
        var theme = Find(themeId);
        var variant = theme.Variants.FirstOrDefault(
            v => string.Equals(v.Id, variantId, StringComparison.OrdinalIgnoreCase)) ?? theme.Variants[0];

        if (_applied && ReferenceEquals(theme, Active) && ReferenceEquals(variant, ActiveVariant) && dark == IsDark)
        {
            return false;
        }

        _applied = true;
        Active = theme;
        ActiveVariant = variant;
        IsDark = dark;

        var p = dark ? theme.Dark : theme.Light;

        Tokens.Colors.Chassis = Tokens.Colors.Rgb(p.Ground);
        Tokens.Colors.Panel = Tokens.Colors.Rgb(p.Surface);
        Tokens.Colors.Deck = Tokens.Colors.Rgb(p.Surface);
        Tokens.Colors.Cap = Tokens.Colors.Rgb(p.SurfaceRaised);
        Tokens.Colors.Seam = Tokens.Colors.Rgb(p.Border);
        Tokens.Colors.Hover = Tokens.Colors.Rgb(p.Hover);
        Tokens.Colors.Ink = Tokens.Colors.Rgb(p.Ink);
        Tokens.Colors.InkOnDeck = Tokens.Colors.Rgb(p.Ink);
        Tokens.Colors.InkSecondary = Tokens.Colors.Rgb(p.InkMuted);
        Tokens.Colors.Silkscreen = Tokens.Colors.Rgb(p.InkMuted);
        Tokens.Colors.OnAccent = Tokens.Colors.Rgb(p.AccentInk);
        Tokens.Colors.PillFill = Tokens.Colors.Argb(p.PillFill);
        Tokens.Colors.PillEdge = Tokens.Colors.Argb(p.PillEdge);
        Tokens.Colors.PillRule = Tokens.Colors.Argb(p.PillRule);
        Tokens.Colors.Glass = Tokens.Colors.PillFill;
        Tokens.Colors.RecordIdle = Tokens.Colors.Hover;

        var accent = Tokens.Colors.Rgb(dark ? variant.Dark : variant.Light);
        Tokens.Colors.Accent = accent;
        Tokens.Colors.AccentSecondary = Tokens.Colors.Rgb(
            (dark ? variant.DarkSecondary : variant.LightSecondary) ?? (dark ? variant.Dark : variant.Light));
        Tokens.Colors.AccentFill = theme.AccentFillFromDark ? Tokens.Colors.Rgb(variant.Dark) : accent;
        Tokens.Colors.AccentTint = Color.FromArgb(dark ? TintAlphaDark : TintAlphaLight, accent.R, accent.G, accent.B);
        Tokens.Colors.Positive = Tokens.Colors.Rgb(dark ? PositiveDark : PositiveLight);
        Tokens.Colors.Caution = Tokens.Colors.Rgb(dark ? CautionDark : CautionLight);

        Tokens.Radius.Chip = theme.ButtonRadius;
        Tokens.Radius.Panel = theme.CardRadius;
        Tokens.Radius.Pill = theme.PillRadius;

        Tokens.Fonts.Grotesque = theme.Body;
        Tokens.Fonts.Prose = theme.Body;
        Tokens.Fonts.Mono = theme.Mono;
        Tokens.Fonts.Display = theme.Display;

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }
}
