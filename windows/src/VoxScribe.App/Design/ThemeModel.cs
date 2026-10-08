using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// One mode (light or dark) of a theme. The first eight are <c>0xRRGGBB</c>; the pill
/// colours carry alpha as <c>0xAARRGGBB</c>.
/// </summary>
internal sealed record Palette(
    uint Ground, uint Surface, uint SurfaceRaised, uint Border, uint Hover,
    uint Ink, uint InkMuted, uint AccentInk,
    uint PillFill, uint PillEdge, uint PillRule);

/// <summary>
/// A curated accent: one value for light mode and one for dark. Orb's gradients carry a
/// second stop; every other theme leaves it null and the primary is used.
/// </summary>
internal sealed record AccentVariant(
    string Id, string Label, uint Light, uint Dark, uint? LightSecondary = null, uint? DarkSecondary = null);

/// <summary>How the navigation is framed.</summary>
internal enum NavStyle
{
    /// <summary>A labelled column on the window ground, hairline on its right.</summary>
    Sidebar,

    /// <summary>A narrow icon rail with captions under the icons.</summary>
    Rail,

    /// <summary>A labelled column floating as a surface card.</summary>
    FloatingCard,
}

/// <summary>How a selected nav item or tab is marked.</summary>
internal enum Selection
{
    /// <summary>Hover fill, ink text.</summary>
    Hover,

    /// <summary>Accent tint fill, accent text.</summary>
    Tint,

    /// <summary>Accent fill, on-accent text.</summary>
    AccentFill,

    /// <summary>Ink fill, ground-coloured text.</summary>
    InkFill,

    /// <summary>Hover fill, ink text, accent bar on the leading edge.</summary>
    Bar,

    /// <summary>No fill, ink text, accent underline.</summary>
    Underline,
}

/// <summary>How the four Home stats are framed.</summary>
internal enum StatStyle
{
    /// <summary>Cells separated by hairlines, ruled top and bottom.</summary>
    Ruled,

    /// <summary>Separate surface cards.</summary>
    Cards,
}

/// <summary>How the hero stat (time saved) stands out.</summary>
internal enum HeroStyle
{
    /// <summary>Accent numerals.</summary>
    Accent,

    /// <summary>Accent italic numerals.</summary>
    AccentItalic,

    /// <summary>Accent-filled cell, on-accent numerals.</summary>
    Fill,

    /// <summary>Accent-tinted cell, accent numerals.</summary>
    Tint,
}

/// <summary>How the recent-dictation rows are framed.</summary>
internal enum ListStyle
{
    /// <summary>Hairline under each row.</summary>
    Hairline,

    /// <summary>Alternate rows on a surface fill.</summary>
    Zebra,

    /// <summary>All rows inside one surface card, hairlines between.</summary>
    Card,

    /// <summary>Dashed rule above each row, log style.</summary>
    Dashed,
}

/// <summary>Which pill silhouette the theme wears.</summary>
internal enum PillKind
{
    /// <summary>Tilted paper slip, pen-stroke level.</summary>
    Paper,

    /// <summary>Bodiless glowing orb, diameter = level.</summary>
    Orb,

    /// <summary>Soft capsule, liquid wave.</summary>
    Tide,

    /// <summary>Square status line, block meter.</summary>
    Mono,

    /// <summary>Win11 flyout, mic halo.</summary>
    Fluent,
}

/// <summary>
/// A theme is data: two palettes, its accents, its faces, and a few layout knobs the shared
/// skeleton reads. Nothing here is a view.
/// </summary>
internal sealed record ThemeDefinition
{
    /// <summary>Settings id.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Label { get; init; }

    /// <summary>One-line description for the Appearance picker.</summary>
    public required string Description { get; init; }

    /// <summary>Light-mode palette.</summary>
    public required Palette Light { get; init; }

    /// <summary>Dark-mode palette.</summary>
    public required Palette Dark { get; init; }

    /// <summary>Curated accents; the first is the default.</summary>
    public required IReadOnlyList<AccentVariant> Variants { get; init; }

    /// <summary>Headlines, wordmark.</summary>
    public required FontFamily Display { get; init; }

    /// <summary>Weight of headlines.</summary>
    public required FontWeight DisplayWeight { get; init; }

    /// <summary>Body and controls.</summary>
    public required FontFamily Body { get; init; }

    /// <summary>Times, keycaps, badges.</summary>
    public required FontFamily Mono { get; init; }

    /// <summary>Face of the Home stat numerals.</summary>
    public required FontFamily StatNumerals { get; init; }

    /// <summary>Weight of the Home stat numerals.</summary>
    public required FontWeight StatWeight { get; init; }

    /// <summary>Home headline size.</summary>
    public required double HeadlineSize { get; init; }

    /// <summary>Home stat numeral size.</summary>
    public required double StatSize { get; init; }

    /// <summary>Fixed Home headline, or null for a time-of-day greeting.</summary>
    public string? Headline { get; init; }

    /// <summary>Cards and wells.</summary>
    public required double CardRadius { get; init; }

    /// <summary>Buttons, chips, nav items.</summary>
    public required double ButtonRadius { get; init; }

    /// <summary>The pill body.</summary>
    public required double PillRadius { get; init; }

    /// <summary>Navigation frame.</summary>
    public required NavStyle Nav { get; init; }

    /// <summary>Navigation column width.</summary>
    public required double NavWidth { get; init; }

    /// <summary>Selected nav item marking.</summary>
    public required Selection NavSelection { get; init; }

    /// <summary>Selected settings tab marking.</summary>
    public required Selection TabSelection { get; init; }

    /// <summary>Home stats frame.</summary>
    public required StatStyle Stats { get; init; }

    /// <summary>Hero stat treatment.</summary>
    public required HeroStyle HeroStat { get; init; }

    /// <summary>Recent rows frame.</summary>
    public required ListStyle List { get; init; }

    /// <summary>Labels, nav, badges and footer set in capitals.</summary>
    public bool Uppercase { get; init; }

    /// <summary>The page area sits on a surface layer with a rounded top-left corner.</summary>
    public bool ContentOnSurface { get; init; }

    /// <summary>The Home header sits in a surface card with a large orb.</summary>
    public bool HeaderCard { get; init; }

    /// <summary>Accent fills use the dark-mode (neon) value in both modes.</summary>
    public bool AccentFillFromDark { get; init; }

    /// <summary>Wordmark, first run (ink). Empty hides the wordmark.</summary>
    public string WordmarkLead { get; init; } = string.Empty;

    /// <summary>Wordmark, accented run.</summary>
    public string WordmarkAccent { get; init; } = string.Empty;

    /// <summary>Wordmark, last run (ink).</summary>
    public string WordmarkTail { get; init; } = string.Empty;

    /// <summary>Wordmark size.</summary>
    public double WordmarkSize { get; init; }

    /// <summary>The accented wordmark run is italic.</summary>
    public bool WordmarkAccentItalic { get; init; }

    /// <summary>Pill silhouette.</summary>
    public required PillKind Pill { get; init; }
}
