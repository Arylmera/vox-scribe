using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// The typefaces the five themes are set in. Four OFL families ship inside the assembly
/// (folder URI + <c>#Family Name</c>, the name embedded in the TTF); Fluent uses the faces
/// Windows 11 already has.
/// </summary>
internal static class FontFaces
{
    private const string Root = "avares://VoxScribe.App/Assets/Fonts/";

    // Each custom family carries a system fallback. Avalonia resolves the bundled face
    // first everywhere that matters (verified directly against the real, Skia-backed font
    // manager — see the Task 3 report); the fallback only engages where no real font
    // manager is behind the request, e.g. a pure headless test host with no platform
    // ever initialised, which cannot load embedded fonts at all and would otherwise throw
    // on every glyph lookup.

    /// <summary>Paper's display serif.</summary>
    public static FontFamily InstrumentSerif { get; } =
        new(Root + "InstrumentSerif#Instrument Serif, Georgia, Times New Roman, serif");

    /// <summary>Paper and Orb body face.</summary>
    public static FontFamily Geist { get; } =
        new(Root + "Geist#Geist, Segoe UI Variable Display, Segoe UI, Helvetica Neue, Arial, sans-serif");

    /// <summary>Paper, Orb and Tide mono face.</summary>
    public static FontFamily GeistMono { get; } =
        new(Root + "GeistMono#Geist Mono, Cascadia Mono, Consolas, Menlo, SF Mono, monospace");

    /// <summary>Tide's display and body face.</summary>
    public static FontFamily Figtree { get; } =
        new(Root + "Figtree#Figtree, Segoe UI Variable Display, Segoe UI, Helvetica Neue, Arial, sans-serif");

    /// <summary>Mono's only face.</summary>
    public static FontFamily JetBrainsMono { get; } =
        new(Root + "JetBrainsMono#JetBrains Mono, Cascadia Mono, Consolas, Menlo, SF Mono, monospace");

    /// <summary>Fluent body text — system font.</summary>
    public static FontFamily SegoeText { get; } = new("Segoe UI Variable Text, Segoe UI, sans-serif");

    /// <summary>Fluent display text — system font.</summary>
    public static FontFamily SegoeDisplay { get; } = new("Segoe UI Variable Display, Segoe UI, sans-serif");

    /// <summary>Fluent mono — system font.</summary>
    public static FontFamily CascadiaMono { get; } = new("Cascadia Mono, Consolas, monospace");

    /// <summary>Every bundled TTF, relative to the assembly root. The embedding test walks this.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        "Assets/Fonts/InstrumentSerif/InstrumentSerif-Regular.ttf",
        "Assets/Fonts/InstrumentSerif/InstrumentSerif-Italic.ttf",
        "Assets/Fonts/Geist/Geist-Regular.ttf",
        "Assets/Fonts/Geist/Geist-Medium.ttf",
        "Assets/Fonts/Geist/Geist-SemiBold.ttf",
        "Assets/Fonts/GeistMono/GeistMono-Regular.ttf",
        "Assets/Fonts/GeistMono/GeistMono-Medium.ttf",
        "Assets/Fonts/Figtree/Figtree-Regular.ttf",
        "Assets/Fonts/Figtree/Figtree-Medium.ttf",
        "Assets/Fonts/Figtree/Figtree-SemiBold.ttf",
        "Assets/Fonts/Figtree/Figtree-Bold.ttf",
        "Assets/Fonts/Figtree/Figtree-ExtraBold.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Medium.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-Bold.ttf",
        "Assets/Fonts/JetBrainsMono/JetBrainsMono-ExtraBold.ttf",
    ];
}
