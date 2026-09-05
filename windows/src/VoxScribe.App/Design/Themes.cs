using Avalonia.Media;

namespace VoxScribe.App.Design;

/// <summary>
/// The three visual themes, applied once at startup. A theme swaps the palette and the
/// prose face; geometry, motion and the record red are law and never move.
/// </summary>
/// <remarks>
/// Applied before any window is built, so views may freely cache brushes made from the
/// themable tokens — unlike the accent, a theme never changes mid-run (Settings says
/// "takes effect at next start"). The doctrine survives every theme: red means recording,
/// green and amber are instrumentation readings, the pill never takes focus.
/// </remarks>
/// <summary>Where the transport (record key, meter, counter, undo) lives in the window.</summary>
public enum TransportDock
{
    /// <summary>A hero instrument panel at the top: oversized counter, stats, wide meter.</summary>
    Hero,

    /// <summary>A hardware deck docked at the bottom, tape-machine style.</summary>
    Bottom,
}

/// <summary>How past dictations are presented.</summary>
public enum TranscriptStyle
{
    /// <summary>A bare list separated by hairlines.</summary>
    Bare,

    /// <summary>Numbered takes on a tape log.</summary>
    TakeLog,

    /// <summary>A day-by-day journal with times in the margin.</summary>
    Journal,
}

/// <summary>
/// The three visual themes, applied once at startup: palette, prose face, and layout traits.
/// </summary>
public static class Themes
{
    /// <summary>Settings value of the default theme.</summary>
    public const string Default = "deep-field";

    /// <summary>Where the active theme docks the transport.</summary>
    public static TransportDock Transport { get; private set; } = TransportDock.Hero;

    /// <summary>How the active theme presents past dictations.</summary>
    public static TranscriptStyle Transcripts { get; private set; } = TranscriptStyle.Bare;

    /// <summary>Whether the navigation rail is shown; the Manuscript navigates by letterhead.</summary>
    public static bool ShowRail { get; private set; } = true;

    /// <summary>Whether the meter is a needle gauge rather than a segment strip.</summary>
    public static bool NeedleGauge { get; private set; }

    /// <summary>Corner radius of the dictation pill — a paper strip is barely rounded.</summary>
    public static double PillRadius { get; private set; } = 30;

    /// <summary>The selectable themes, in display order: settings id and label.</summary>
    public static readonly (string Id, string Label)[] Choices =
    [
        ("deep-field", "DEEP FIELD"),
        ("signal-house", "SIGNAL HOUSE"),
        ("manuscript", "MANUSCRIPT"),
    ];

    /// <summary>The theme currently painted, so Settings can tell "saved" from "running".</summary>
    public static string ActiveId { get; private set; } = Default;

    /// <summary>Installs the theme named in settings; an unknown id gets the default.</summary>
    public static void Apply(string? id)
    {
        switch (id)
        {
            case "signal-house": SignalHouse(); break;
            case "manuscript": Manuscript(); break;
            default: id = Default; DeepField(); break;
        }

        ActiveId = id;
    }

    /// <summary>
    /// Void Glass, solidified: the same dark room with opaque surfaces and a slightly
    /// lifted ink. The default.
    /// </summary>
    private static void DeepField()
    {
        Set(
            chassis: 0x0E1116, panel: 0x141922, deck: 0x0A0D11, cap: 0x161B22,
            seam: 0x232A33, hover: 0x1A2029,
            ink: 0xDCE4EC, inkSecondary: 0x7E8994, inkOnDeck: 0xC7D0D9,
            recordIdle: 0x3D2426, meterFace: 0x0A0D11,
            meterGreen: 0x4FE8A0, meterAmber: 0xE8B44F);
        Tokens.Colors.Glass = Color.FromArgb(0x8C, 0x0C, 0x10, 0x16);
        Tokens.Colors.Specular = Colors.White;
        Tokens.Fonts.Prose = Tokens.Fonts.Grotesque;

        Transport = TransportDock.Hero;
        Transcripts = TranscriptStyle.Bare;
        ShowRail = true;
        NeedleGauge = false;
        PillRadius = 12;
    }

    /// <summary>Warm broadcast hardware: charcoal panels, cream ink, tape-machine mood.</summary>
    private static void SignalHouse()
    {
        Set(
            chassis: 0x211C17, panel: 0x261F19, deck: 0x16110D, cap: 0x2B241E,
            seam: 0x3E362C, hover: 0x2E2620,
            ink: 0xEBE1CC, inkSecondary: 0xA38F6F, inkOnDeck: 0xE8DFC9,
            recordIdle: 0x3D2426, meterFace: 0x0F0C09,
            meterGreen: 0x9CC96A, meterAmber: 0xD9A441);
        Tokens.Colors.Glass = Color.FromArgb(0x9E, 0x17, 0x12, 0x0D);
        Tokens.Colors.Specular = Color.FromRgb(0xFF, 0xF3, 0xDC);
        Tokens.Fonts.Prose = Tokens.Fonts.Grotesque;

        Transport = TransportDock.Bottom;
        Transcripts = TranscriptStyle.TakeLog;
        ShowRail = true;
        NeedleGauge = true;
        PillRadius = 30;
    }

    /// <summary>Paper and ink: the one light theme, with transcripts set in a serif.</summary>
    private static void Manuscript()
    {
        Set(
            chassis: 0xEDEAE3, panel: 0xF7F5EF, deck: 0xE7E3D8, cap: 0xEFECE3,
            seam: 0xD8D3C6, hover: 0xEFECE3,
            ink: 0x23262B, inkSecondary: 0x8A8578, inkOnDeck: 0x23262B,
            recordIdle: 0xC9ADAD, meterFace: 0xE7E3D8,
            meterGreen: 0x2E9C6B, meterAmber: 0xC98A3D);
        Tokens.Colors.Glass = Color.FromArgb(0xE6, 0xF7, 0xF5, 0xEF);
        // Dark specular: on paper, edges and lens highlights are drawn in ink.
        Tokens.Colors.Specular = Color.FromRgb(0x23, 0x26, 0x2B);
        Tokens.Fonts.Prose = new FontFamily("Georgia, Times New Roman, serif");

        Transport = TransportDock.Bottom;
        Transcripts = TranscriptStyle.Journal;
        ShowRail = false;
        NeedleGauge = false;
        PillRadius = 4;
    }

    private static void Set(
        uint chassis, uint panel, uint deck, uint cap, uint seam, uint hover,
        uint ink, uint inkSecondary, uint inkOnDeck,
        uint recordIdle, uint meterFace, uint meterGreen, uint meterAmber)
    {
        Tokens.Colors.Chassis = Tokens.Colors.Rgb(chassis);
        Tokens.Colors.Panel = Tokens.Colors.Rgb(panel);
        Tokens.Colors.Deck = Tokens.Colors.Rgb(deck);
        Tokens.Colors.Cap = Tokens.Colors.Rgb(cap);
        Tokens.Colors.Seam = Tokens.Colors.Rgb(seam);
        Tokens.Colors.Hover = Tokens.Colors.Rgb(hover);
        Tokens.Colors.Ink = Tokens.Colors.Rgb(ink);
        Tokens.Colors.InkSecondary = Tokens.Colors.Rgb(inkSecondary);
        Tokens.Colors.Silkscreen = Tokens.Colors.Rgb(inkSecondary);
        Tokens.Colors.InkOnDeck = Tokens.Colors.Rgb(inkOnDeck);
        Tokens.Colors.RecordIdle = Tokens.Colors.Rgb(recordIdle);
        Tokens.Colors.MeterFace = Tokens.Colors.Rgb(meterFace);
        Tokens.Colors.MeterGreen = Tokens.Colors.Rgb(meterGreen);
        Tokens.Colors.MeterAmber = Tokens.Colors.Rgb(meterAmber);
    }
}
