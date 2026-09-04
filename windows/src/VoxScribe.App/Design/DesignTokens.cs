using Avalonia.Media;

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
/// <para>
/// The line between a token and a private constant: a token is a decision the <i>system</i>
/// makes and more than one control must agree on. A number that only exists inside one
/// control's <c>Render</c> — where a specular dot sits on a lens, how far a bar breathes —
/// is that control's own arithmetic and belongs to it as a named private constant. Hoisting
/// those here would make the system look bigger than the decisions it actually holds.
/// </para>
/// <para>One rule that is not negotiable: <b>red means recording.</b> Nothing else is red.</para>
/// </remarks>
public static class Tokens
{
    // ---- Colour ----

    /// <summary>Surfaces, from the window ground inward.</summary>
    public static class Colors
    {
        /// <summary>The window ground. Cool near-black; frames everything.</summary>
        public static Color Chassis => Rgb(0x0A0D12);

        /// <summary>A glass card resting on the ground.</summary>
        public static Color Panel => Rgb(0x12161D);

        /// <summary>The darkest readout surface (counters, inputs, lists).</summary>
        public static Color Deck => Rgb(0x0B0F14);

        /// <summary>Buttons and interactive chips.</summary>
        public static Color Cap => Rgb(0x1A212B);

        /// <summary>Hairline border between surfaces.</summary>
        public static Color Seam => Rgb(0x232A35);

        /// <summary>Row under the pointer, before selection.</summary>
        public static Color Hover => Rgb(0x171D26);

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

        /// <summary>
        /// The dictation pill's fill: near-black at ~55% alpha, so the desktop shows through.
        /// </summary>
        /// <remarks>
        /// Carries its own alpha rather than taking one from <see cref="Emphasis"/>: this is a
        /// material, not a de-emphasised ink, and the pill is the only thing wearing it.
        /// </remarks>
        public static Color Glass => Color.FromArgb(0x8C, 0x0C, 0x10, 0x16);

        /// <summary>Lens highlights and glass edges. Always used with an opacity.</summary>
        public static Color Specular => Avalonia.Media.Colors.White;

        /// <summary>
        /// The user's accent, from settings. Mutable on purpose: set at startup and whenever
        /// settings change; controls that repaint per frame pick it up immediately.
        /// </summary>
        /// <remarks>
        /// Because it moves, <b>nothing may cache a brush made from it</b>. Build the brush at
        /// paint time, or a stale accent survives until the control is rebuilt.
        /// </remarks>
        public static Color Accent { get; set; } = Color.FromRgb(0x4F, 0xD8, 0xE8);

        // Instrumentation colours. Green and amber are readings — a level, a verdict, a
        // correction that fired — and never UI chrome.

        /// <summary>The level strip's dark backing.</summary>
        public static Color MeterFace => Rgb(0x0B0F14);

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

        /// <inheritdoc cref="Colors.Deck"/>
        public static IBrush Deck => new SolidColorBrush(Colors.Deck);

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

        /// <summary>Ink at a chosen level of de-emphasis. See <see cref="Emphasis"/>.</summary>
        public static IBrush InkOnDeckAt(double emphasis) =>
            new SolidColorBrush(Colors.InkOnDeck, emphasis);
    }

    /// <summary>
    /// How far something recedes, as an opacity.
    /// </summary>
    /// <remarks>
    /// A ladder rather than a number per call site. Six hand-picked alphas between 0.3 and
    /// 0.65 once did this job and no two of them were meaningfully different to the eye —
    /// which is exactly how a design system rots. Pick the rung that matches the intent.
    /// </remarks>
    public static class Emphasis
    {
        /// <summary>A control's own label: quieter than body text, still clearly a control.</summary>
        public const double Muted = 0.65;

        /// <summary>Labels, counts, tags, timestamps — present but not competing.</summary>
        public const double Soft = 0.5;

        /// <summary>Something switched off but still listed.</summary>
        public const double Disabled = 0.45;

        /// <summary>Explanatory copy under a heading.</summary>
        public const double Ghost = 0.4;

        /// <summary>A hairline edge drawn on the deck.</summary>
        public const double Outline = 0.3;
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
        /// <summary>Indicator chips, small lamps, badges.</summary>
        public const double Chip = 8;

        /// <summary>Glass cards and recessed wells.</summary>
        public const double Panel = 14;

        /// <summary>Navigation rail keys.</summary>
        public const double RailKey = 12;
    }

    /// <summary>Line weights. All 1 — a machined edge reads the same at any density.</summary>
    public static class Border
    {
        /// <summary>A drawn hairline.</summary>
        public const double Hairline = 1;

        /// <summary>The seam between two panels.</summary>
        public const double Seam = 1;

        /// <summary>The ring around the selected accent swatch.</summary>
        public const double Ring = 2;
    }

    // ---- Material ----

    /// <summary>The fixed dimensions of the app's own furniture.</summary>
    public static class Material
    {
        /// <summary>Indicator lamp diameter.</summary>
        public const double LampSize = 7;

        /// <summary>A lamp shrunk to a bullet beside a line of text.</summary>
        public const double LampBullet = 6;

        /// <summary>How far an unlit lamp sits below the lit value.</summary>
        public const double LampUnlitOpacity = 0.22;

        /// <summary>A lit lamp's lens highlight — a specular dot, not a bloom.</summary>
        public const double LampSpecular = 0.45;

        /// <summary>Width of the navigation rail on the left edge.</summary>
        public const double RailWidth = 64;

        /// <summary>A square rail key (icon button).</summary>
        public const double RailKeySize = 40;

        /// <summary>Stroke-icon canvas inside a rail key.</summary>
        public const double RailIconSize = 18;

        /// <summary>Stroke weight of rail icons.</summary>
        public const double RailIconStroke = 1.7;

        /// <summary>The app badge at the head of the rail.</summary>
        public const double BadgeSize = 26;

        /// <summary>The mark inside the app badge.</summary>
        public const double BadgeIconSize = 14;

        /// <summary>Stroke weight of the badge mark — heavier than a rail icon, it is smaller.</summary>
        public const double BadgeIconStroke = 2.2;

        /// <summary>The round record button in the voice band.</summary>
        public const double RecordKeySize = 44;

        /// <summary>The record button's lens (lamp) diameter.</summary>
        public const double RecordLensSize = 12;

        /// <summary>Height of the custom title strip; also the extended-chrome hint.</summary>
        public const double TitleBarHeight = 44;

        /// <summary>Space reserved right of the title strip for the system caption buttons.</summary>
        public const double CaptionButtonsReserve = 140;

        /// <summary>Transport key height.</summary>
        public const double KeyHeight = 34;

        /// <summary>Minimum transport key width.</summary>
        public const double KeyMinWidth = 52;

        /// <summary>An accent swatch in Settings.</summary>
        public const double SwatchSize = 30;

        /// <summary>Width of the FIX / TERM tag column, so the words beside them line up.</summary>
        public const double EntryTagWidth = 34;

        /// <summary>Widest a warning line may run before it wraps.</summary>
        public const double WarningMaxWidth = 340;

        /// <summary>
        /// How strongly an instrumentation colour tints the outline of a notice — the
        /// "corrected" chips and the dictionary's false-positive warnings. Amber at full
        /// strength around a box reads as an error; this reads as a note.
        /// </summary>
        public const double NoticeEdgeOpacity = 0.4;

        /// <summary>Opacity of the pill's glass edge when not recording.</summary>
        public const double GlassEdgeOpacity = 0.14;

        /// <summary>The dictation pill's lamp — smaller than a panel lamp.</summary>
        public const double PillLampSize = 7;

        /// <summary>Height of the level bars inside the pill.</summary>
        public const double PillBarsHeight = 30;

        /// <summary>Corner radius of the pill: a full round end at its compact height.</summary>
        public const double PillRadius = 30;

        /// <summary>How far the pill sits above the bottom of the working area.</summary>
        public const double PillScreenMargin = 24;
    }

    // ---- Motion ----

    /// <summary>Mechanical, not bouncy. A key travels and stops; it doesn't spring.</summary>
    public static class Motion
    {
        /// <summary>How long a transient status line stays before it reverts.</summary>
        public static TimeSpan StatusHold { get; } = TimeSpan.FromSeconds(2);

        /// <summary>How long "COPIED" replaces "COPY" on a transcript row.</summary>
        public static TimeSpan CopyHold { get; } = TimeSpan.FromMilliseconds(1400);

        /// <summary>View entrance fade-in.</summary>
        public static TimeSpan FadeIn { get; } = TimeSpan.FromMilliseconds(300);

        /// <summary>Opacity a view fades in from. Close to 1: a hint of arrival, not a reveal.</summary>
        public const double FadeInFrom = 0.9;

        /// <summary>Opacity a settings section fades in from.</summary>
        public const double SectionFadeInFrom = 0.8;

        /// <summary>Display refresh for the pill — ~30 fps, which is all a readout needs.</summary>
        public static TimeSpan PillFrame { get; } = TimeSpan.FromMilliseconds(33);

        /// <summary>
        /// How long the pill stays up after a dictation that ended with a failure notice —
        /// long enough to read one short sentence, short enough not to nag.
        /// </summary>
        public static TimeSpan NoticeLinger { get; } = TimeSpan.FromSeconds(3);

        /// <summary>Display refresh for the VU movement — ~60 fps, so the needle is smooth.</summary>
        public static TimeSpan MeterFrame { get; } = TimeSpan.FromMilliseconds(16);

        /// <summary>How often the main window polls the engine for level and elapsed time.</summary>
        public static TimeSpan PanelPoll { get; } = TimeSpan.FromMilliseconds(100);

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

    /// <summary>Window sizes.</summary>
    public static class Size
    {
        /// <summary>Main window, initial width.</summary>
        public const double MainWidth = 880;

        /// <summary>Main window, initial height.</summary>
        public const double MainHeight = 640;

        /// <summary>Narrowest the main window may be dragged before the rail crowds the content.</summary>
        public const double MainMinWidth = 720;

        /// <summary>Shortest the main window may be dragged.</summary>
        public const double MainMinHeight = 520;

        /// <summary>Settings window, initial width.</summary>
        public const double SettingsWidth = 540;

        /// <summary>Settings window, initial height — under a laptop screen, so it scrolls.</summary>
        public const double SettingsHeight = 720;

        /// <summary>Narrowest the settings window may be dragged.</summary>
        public const double SettingsMinWidth = 480;

        /// <summary>Shortest the settings window may be dragged.</summary>
        public const double SettingsMinHeight = 480;

        /// <summary>The dictionary entry editor. Height follows its content.</summary>
        public const double EditorWidth = 460;

        /// <summary>The dictation pill.</summary>
        public const double PillWidth = 380;

        /// <summary>Pill height with the readout row only.</summary>
        public const double PillCompactHeight = 60;

        /// <summary>Pill height once the transcript preview line is showing.</summary>
        public const double PillPreviewHeight = 100;
    }
}
