using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace VoxScribe.App.Design;

/// <summary>
/// The design system, in one place — the <b>Void Glass</b> direction.
/// </summary>
/// <remarks>
/// <para>
/// Cool near-black ground, translucent glass cards, generous radii, and one user-selected
/// accent that tints every highlight. Modern and quiet; depth comes from layered
/// translucency and hairline borders, never bevels or grain.
/// </para>
/// <para>
/// <b>Views must not contain literal values.</b> If a control needs a number that isn't here,
/// add the token rather than inlining it.
/// </para>
/// <para>One rule that is not negotiable: <b>red means recording.</b> Nothing else is red.</para>
/// </remarks>
public static class Tokens
{
    /// <summary>
    /// Void Glass is a single dark finish; kept for call sites that still branch on it.
    /// </summary>
    public static bool IsBlackFace => true;

    // ---- Colour ----

    /// <summary>Surfaces, from the window ground inward.</summary>
    public static class Colors
    {
        /// <summary>The window ground. Cool near-black; frames everything.</summary>
        public static Color Chassis => Rgb(0x0A0D12);

        /// <summary>A glass card resting on the ground.</summary>
        public static Color Panel => Rgb(0x12161D);

        /// <summary>Lifted edge of a glass card (hover, subtle emphasis).</summary>
        public static Color PanelHighlight => Rgb(0x1D232C);

        /// <summary>Recess behind a card.</summary>
        public static Color PanelShade => Rgb(0x0D1015);

        /// <summary>Recessed wells, set into the ground.</summary>
        public static Color Well => Rgb(0x0E1218);

        /// <summary>The darkest readout surface (counters, inputs).</summary>
        public static Color Deck => Rgb(0x0B0F14);

        /// <summary>Buttons and interactive chips.</summary>
        public static Color Cap => Rgb(0x1A212B);

        /// <summary>Hairline border between surfaces.</summary>
        public static Color Seam => Rgb(0x232A35);

        /// <summary>Primary readable text.</summary>
        public static Color Ink => Rgb(0xE9EDF2);

        /// <summary>Supporting text.</summary>
        public static Color InkSecondary => Rgb(0x8B96A5);

        /// <summary>Section labels and captions.</summary>
        public static Color Silkscreen => Rgb(0x8B96A5);

        /// <summary>Text on the darkest readout surface.</summary>
        public static Color InkOnDeck => Rgb(0xE9EDF2);

        /// <summary>The record indicator. The only red in the app.</summary>
        public static Color Record => Rgb(0xE85656);

        /// <summary>The record indicator unlit — a dark lens, not an absence.</summary>
        public static Color RecordIdle => Rgb(0x3D2426);

        /// <summary>A selected row.</summary>
        public static Color Selection => Rgb(0x1A212B);

        /// <summary>Edge on a selected or focused element.</summary>
        public static Color SelectionEdge => Rgb(0x2C3542);

        /// <summary>Keyboard focus ring. Reads without relying on colour.</summary>
        public static Color FocusRing => Rgb(0x3A4656);

        /// <summary>Row under the pointer, before selection.</summary>
        public static Color Hover => Rgb(0x171D26);

        /// <summary>
        /// The user's accent, from settings (Void Glass redesign). Mutable on purpose:
        /// set at startup and whenever settings change; controls that repaint per frame
        /// (the HUD bars) pick it up immediately.
        /// </summary>
        public static Color Accent { get; set; } = Color.FromRgb(0x4F, 0xD8, 0xE8);

        // Status colours: green = healthy, amber = attention, red = over/error.

        /// <summary>The level strip's dark backing.</summary>
        public static Color MeterFace => Rgb(0x0B0F14);

        /// <summary>Unlit level segment.</summary>
        public static Color MeterLamp => Rgb(0x1D232C);

        /// <summary>Level strip tick printing.</summary>
        public static Color MeterNeedle => Rgb(0x3A4656);

        /// <summary>Healthy / nominal.</summary>
        public static Color MeterGreen => Rgb(0x4FE8A0);

        /// <summary>Attention / approaching peak.</summary>
        public static Color MeterAmber => Rgb(0xE8B44F);

        /// <summary>Over / error.</summary>
        public static Color MeterRed => Rgb(0xE85656);

        private static Color Rgb(uint hex) => Color.FromRgb(
            (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
    }

    /// <summary>Brushes for the colours above, allocated per call.</summary>
    public static class Brushes
    {
        /// <inheritdoc cref="Colors.Chassis"/>
        public static IBrush Chassis => new SolidColorBrush(Colors.Chassis);

        /// <inheritdoc cref="Colors.Panel"/>
        public static IBrush Panel => new SolidColorBrush(Colors.Panel);

        /// <inheritdoc cref="Colors.Well"/>
        public static IBrush Well => new SolidColorBrush(Colors.Well);

        /// <inheritdoc cref="Colors.Deck"/>
        public static IBrush Deck => new SolidColorBrush(Colors.Deck);

        /// <inheritdoc cref="Colors.Cap"/>
        public static IBrush Cap => new SolidColorBrush(Colors.Cap);

        /// <inheritdoc cref="Colors.Ink"/>
        public static IBrush Ink => new SolidColorBrush(Colors.Ink);

        /// <inheritdoc cref="Colors.Silkscreen"/>
        public static IBrush Silkscreen => new SolidColorBrush(Colors.Silkscreen);

        /// <inheritdoc cref="Colors.InkOnDeck"/>
        public static IBrush InkOnDeck => new SolidColorBrush(Colors.InkOnDeck);

        /// <inheritdoc cref="Colors.Record"/>
        public static IBrush Record => new SolidColorBrush(Colors.Record);

        /// <inheritdoc cref="Colors.MeterFace"/>
        public static IBrush MeterFace => new SolidColorBrush(Colors.MeterFace);
    }

    // ---- Type ----

    /// <summary>
    /// A modern grotesque for the glass surfaces.
    /// </summary>
    /// <remarks>
    /// Segoe UI Variable is the closest widely-installed face to the mockups' Space Grotesk;
    /// Cascadia Mono echoes IBM Plex Mono for readouts.
    /// </remarks>
    public static class Fonts
    {
        /// <summary>The interface typeface.</summary>
        public static FontFamily Grotesque { get; } =
            new("Segoe UI Variable Display, Segoe UI, Helvetica Neue, Arial, sans-serif");

        /// <summary>Readouts and timings. Monospaced so digits don't shift as they tick.</summary>
        public static FontFamily Mono { get; } =
            new("Cascadia Mono, Consolas, Menlo, SF Mono, monospace");

        /// <summary>Panel labels: small, uppercase, tightly tracked.</summary>
        public const double Silkscreen = 9;

        /// <summary>A larger silkscreen label, for section headers.</summary>
        public const double SilkscreenLarge = 11;

        /// <summary>Caption text.</summary>
        public const double Caption = 10;

        /// <summary>Secondary label text.</summary>
        public const double Label = 11;

        /// <summary>Body text.</summary>
        public const double Body = 13;

        /// <summary>Section titles.</summary>
        public const double Title = 17;

        /// <summary>The big transport counter.</summary>
        public const double CounterLarge = 26;

        /// <summary>Letter spacing for silkscreen labels, in device-independent pixels.</summary>
        public const double SilkscreenTracking = 1.1;
    }

    // ---- Geometry ----

    /// <summary>A 4pt grid. Panels are laid out on it; nothing sits between steps.</summary>
    public static class Space
    {
        /// <summary>2</summary>
        public const double Hair = 2;

        /// <summary>4</summary>
        public const double Tight = 4;

        /// <summary>8</summary>
        public const double Snug = 8;

        /// <summary>12</summary>
        public const double Base = 12;

        /// <summary>16</summary>
        public const double Roomy = 16;

        /// <summary>24</summary>
        public const double Wide = 24;

        /// <summary>32</summary>
        public const double Panel = 32;
    }

    /// <summary>
    /// Generous by design — Void Glass surfaces are soft-cornered cards.
    /// </summary>
    public static class Radius
    {
        /// <summary>Seams and dividers — square.</summary>
        public const double None = 0;

        /// <summary>Indicator chips, small lamps.</summary>
        public const double Chip = 8;

        /// <summary>Buttons and controls — full pill at control heights.</summary>
        public const double Control = 17;

        /// <summary>Glass cards and recessed wells.</summary>
        public const double Panel = 14;

        /// <summary>The window itself.</summary>
        public const double Window = 18;
    }

    /// <summary>Line weights. All 1 — a machined edge reads the same at any density.</summary>
    public static class Border
    {
        /// <summary>A drawn hairline.</summary>
        public const double Hairline = 1;

        /// <summary>The seam between two panels.</summary>
        public const double Seam = 1;

        /// <summary>Bevel thickness on raised controls.</summary>
        public const double Bevel = 1;
    }

    // ---- Material ----

    /// <summary>
    /// The physical detail that makes a panel read as a machined object: metal grain,
    /// fasteners, ventilation, lamps, key travel, needle sweep.
    /// </summary>
    public static class Material
    {
        /// <summary>Opacity of the lighter striations in brushed metal.</summary>
        public const double GrainLight = 0.055;

        /// <summary>Opacity of the darker striations.</summary>
        public const double GrainDark = 0.07;

        /// <summary>Distance between striations.</summary>
        public const double GrainPitch = 2;

        /// <summary>Diameter of a panel screw head.</summary>
        public const double ScrewSize = 9;

        /// <summary>A single vent slot.</summary>
        public const double VentSlotWidth = 3;

        /// <summary>Height of a vent slot.</summary>
        public const double VentSlotHeight = 22;

        /// <summary>Gap between vent slots.</summary>
        public const double VentSlotGap = 4;

        /// <summary>Indicator lamp diameter.</summary>
        public const double LampSize = 7;

        /// <summary>A lit lamp's lens highlight — a specular dot, not a bloom.</summary>
        public const double LampSpecular = 0.45;

        /// <summary>How far an unlit lamp sits below the lit value.</summary>
        public const double LampUnlitOpacity = 0.22;

        /// <summary>Transport key height.</summary>
        public const double KeyHeight = 34;

        /// <summary>Minimum transport key width.</summary>
        public const double KeyMinWidth = 52;

        /// <summary>How far a key sinks when pressed.</summary>
        public const double KeyTravel = 1.5;

        /// <summary>Total sweep of the VU needle, in degrees, centred on vertical.</summary>
        public const double NeedleSweepDegrees = 96;

        /// <summary>Needle thickness.</summary>
        public const double NeedleWidth = 1.5;

        /// <summary>Where 0 VU sits along the scale, 0…1. The red zone begins here.</summary>
        public const double MeterZeroPoint = 0.72;
    }

    // ---- Motion ----

    /// <summary>Mechanical, not bouncy. A key travels and stops; it doesn't spring.</summary>
    public static class Motion
    {
        /// <summary>Key travel down. Fast enough to feel like contact.</summary>
        public static TimeSpan Press { get; } = TimeSpan.FromMilliseconds(60);

        /// <summary>Key travel up.</summary>
        public static TimeSpan Release { get; } = TimeSpan.FromMilliseconds(120);

        /// <summary>Panel and view changes.</summary>
        public static TimeSpan Panel { get; } = TimeSpan.FromMilliseconds(180);

        /// <summary>The record lamp coming on — instant, like a filament.</summary>
        public static TimeSpan Lamp { get; } = TimeSpan.FromMilliseconds(80);

        /// <summary>
        /// VU ballistics: seconds to reach a step going up.
        /// </summary>
        /// <remarks>
        /// A real VU movement takes ~300 ms, but that read as sluggish against live speech —
        /// the needle is deliberately snappier than the instrument it imitates, while the
        /// slower release below keeps the ballistic fall that gives it character.
        /// </remarks>
        public const double NeedleAttackSeconds = 0.14;

        /// <summary>Seconds for the needle to fall back.</summary>
        public const double NeedleReleaseSeconds = 0.42;

        /// <summary>Peak overshoot as a fraction of the step, before settling.</summary>
        public const double NeedleOvershoot = 0.06;

        /// <summary>
        /// Display gain applied to the raw RMS before the perceptual sqrt. Speech RMS lives
        /// around 0.02–0.15, so without this the meter and HUD bars barely leave the floor.
        /// Display-only — the audio itself is untouched.
        /// </summary>
        public const double LevelGain = 2.5;
    }
}
