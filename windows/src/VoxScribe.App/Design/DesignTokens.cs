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
/// <para>Red is used only for the recording dot. Nothing else is red.</para>
/// </remarks>
public static class Tokens
{
    // ---- Colour ----

    /// <summary>
    /// Surfaces, inks and accents. Settable values belong to the active theme and are written by
    /// <see cref="Themes.Apply"/>; defaults equal Paper light. <see cref="Record"/> is law.
    /// </summary>
    public static class Colors
    {
        /// <summary>The window ground (theme <c>ground</c>).</summary>
        public static Color Chassis { get; internal set; } = Rgb(0xF4EFE6);

        /// <summary>A card on the ground (theme <c>surface</c>).</summary>
        public static Color Panel { get; internal set; } = Rgb(0xFBF8F2);

        /// <summary>Lists and inputs (theme <c>surface</c>).</summary>
        public static Color Deck { get; internal set; } = Rgb(0xFBF8F2);

        /// <summary>Buttons and raised chips (theme <c>surfaceRaised</c>).</summary>
        public static Color Cap { get; internal set; } = Rgb(0xFFFFFF);

        /// <summary>Hairline border (theme <c>border</c>).</summary>
        public static Color Seam { get; internal set; } = Rgb(0xE3DBCD);

        /// <summary>Row under the pointer, selected nav (theme <c>hover</c>).</summary>
        public static Color Hover { get; internal set; } = Rgb(0xEAE2D3);

        /// <summary>Primary text (theme <c>ink</c>).</summary>
        public static Color Ink { get; internal set; } = Rgb(0x1E1A15);

        /// <summary>Supporting text (theme <c>inkMuted</c>).</summary>
        public static Color InkSecondary { get; internal set; } = Rgb(0x6B6153);

        /// <summary>Section labels (theme <c>inkMuted</c>).</summary>
        public static Color Silkscreen { get; internal set; } = Rgb(0x6B6153);

        /// <summary>Text on lists and inputs (theme <c>ink</c>).</summary>
        public static Color InkOnDeck { get; internal set; } = Rgb(0x1E1A15);

        /// <summary>
        /// The recording dot. The only red in the app, identical in every theme, and nothing
        /// else may use it.
        /// </summary>
        public static Color Record => Rgb(0xE5484D);

        /// <summary>Accent for text and strokes on the ground (variant, by mode).</summary>
        public static Color Accent { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Second gradient stop (Orb); equals <see cref="Accent"/> elsewhere.</summary>
        public static Color AccentSecondary { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Accent used as a fill behind <see cref="OnAccent"/> text.</summary>
        public static Color AccentFill { get; internal set; } = Rgb(0x6A3D9A);

        /// <summary>Text and glyphs on <see cref="AccentFill"/> (theme <c>accentInk</c>).</summary>
        public static Color OnAccent { get; internal set; } = Rgb(0xFFFFFF);

        /// <summary>The accent at ~10% alpha, for tinted fills.</summary>
        public static Color AccentTint { get; internal set; } = Color.FromArgb(0x1A, 0x6A, 0x3D, 0x9A);

        /// <summary>The dictation pill's body (may carry alpha).</summary>
        public static Color PillFill { get; internal set; } = Argb(0xFFFBF8F2);

        /// <summary>The pill's edge (may carry alpha; transparent for Tide).</summary>
        public static Color PillEdge { get; internal set; } = Argb(0xFFE3DBCD);

        /// <summary>Rules and unlit cells inside the pill.</summary>
        public static Color PillRule { get; internal set; } = Argb(0xFFD9CFBE);

        /// <summary>A good outcome (connection OK, model found). Never red.</summary>
        public static Color Positive { get; internal set; } = Rgb(0x107C10);

        /// <summary>A warning or a failure. Never red: red means recording.</summary>
        public static Color Caution { get; internal set; } = Rgb(0x8A5200);

        /// <summary>Old pill body; removed with the old pill in Task 9.</summary>
        public static Color Glass { get; internal set; } = Argb(0xFFFBF8F2);

        /// <summary>Old pill idle lamp; removed with the old pill in Task 9.</summary>
        public static Color RecordIdle { get; internal set; } = Rgb(0xEAE2D3);

        /// <summary>Lens highlights. Always used with an opacity.</summary>
        public static Color Specular { get; internal set; } = Avalonia.Media.Colors.White;

        /// <summary>WCAG 2.1 contrast ratio, 1:1 to 21:1. AA text needs 4.5:1, large text 3:1.</summary>
        public static double GetContrastRatio(Color foreground, Color background)
        {
            var l1 = GetRelativeLuminance(foreground);
            var l2 = GetRelativeLuminance(background);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        private static double GetRelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        }

        internal static Color Rgb(uint hex) => Color.FromRgb(
            (byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));

        internal static Color Argb(uint hex) => Color.FromUInt32(hex);
    }

    /// <summary>Brushes for the colours above, allocated per call so none outlives a theme change.</summary>
    public static class Brushes
    {
        /// <inheritdoc cref="Colors.Chassis"/>
        public static IBrush Chassis => new SolidColorBrush(Colors.Chassis);

        /// <inheritdoc cref="Colors.Panel"/>
        public static IBrush Panel => new SolidColorBrush(Colors.Panel);

        /// <inheritdoc cref="Colors.Deck"/>
        public static IBrush Deck => new SolidColorBrush(Colors.Deck);

        /// <inheritdoc cref="Colors.Seam"/>
        public static IBrush Seam => new SolidColorBrush(Colors.Seam);

        /// <inheritdoc cref="Colors.Hover"/>
        public static IBrush Hover => new SolidColorBrush(Colors.Hover);

        /// <inheritdoc cref="Colors.Ink"/>
        public static IBrush Ink => new SolidColorBrush(Colors.Ink);

        /// <inheritdoc cref="Colors.InkSecondary"/>
        public static IBrush InkSecondary => new SolidColorBrush(Colors.InkSecondary);

        /// <inheritdoc cref="Colors.Silkscreen"/>
        public static IBrush Silkscreen => new SolidColorBrush(Colors.Silkscreen);

        /// <inheritdoc cref="Colors.InkOnDeck"/>
        public static IBrush InkOnDeck => new SolidColorBrush(Colors.InkOnDeck);

        /// <inheritdoc cref="Colors.Accent"/>
        public static IBrush Accent => new SolidColorBrush(Colors.Accent);

        /// <inheritdoc cref="Colors.AccentFill"/>
        public static IBrush AccentFill => new SolidColorBrush(Colors.AccentFill);

        /// <inheritdoc cref="Colors.OnAccent"/>
        public static IBrush OnAccent => new SolidColorBrush(Colors.OnAccent);

        /// <inheritdoc cref="Colors.Record"/>
        public static IBrush Record => new SolidColorBrush(Colors.Record);

        /// <summary>Ink at a chosen level of de-emphasis. See <see cref="Emphasis"/>.</summary>
        public static IBrush InkOnDeckAt(double emphasis) => new SolidColorBrush(Colors.InkOnDeck, emphasis);
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
        /// <summary>The active theme's body face. (The name is historical; it is not always a grotesque.)</summary>
        public static FontFamily Grotesque { get; internal set; } = FontFaces.Geist;

        /// <summary>The active theme's display face: headlines, wordmark.</summary>
        public static FontFamily Display { get; internal set; } = FontFaces.InstrumentSerif;

        /// <summary>The spoken word — transcript rows. Follows the theme's body face.</summary>
        public static FontFamily Prose { get; internal set; } = FontFaces.Geist;

        /// <summary>Readouts and timings. Monospaced so digits don't shift as they tick.</summary>
        public static FontFamily Mono { get; internal set; } = FontFaces.GeistMono;

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

        /// <summary>Row text, nav labels.</summary>
        public const double Row = 14;

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

    /// <summary>Corner radii. Chip and Panel follow the theme (Mono is square, Paper is round).</summary>
    public static class Radius
    {
        /// <summary>Buttons, chips, badges, nav items (theme <c>radius.button</c>).</summary>
        public static double Chip { get; internal set; } = 999;

        /// <summary>Cards and wells (theme <c>radius.card</c>).</summary>
        public static double Panel { get; internal set; } = 12;

        /// <summary>The pill body (theme <c>radius.pill</c>).</summary>
        public static double Pill { get; internal set; } = 3;
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

        /// <summary>Height of the custom title strip; also the extended-chrome hint.</summary>
        public const double TitleBarHeight = 44;

        /// <summary>A sidebar nav item's height.</summary>
        public const double NavItemHeight = 40;

        /// <summary>A rail nav item (Orb): width.</summary>
        public const double RailItemWidth = 60;

        /// <summary>A rail nav item (Orb): height, icon over caption.</summary>
        public const double RailItemHeight = 56;

        /// <summary>Nav icon canvas.</summary>
        public const double NavIconSize = 18;

        /// <summary>Nav icon stroke.</summary>
        public const double NavIconStroke = 1.6;

        /// <summary>A recent-dictation row on Home.</summary>
        public const double RowHeight = 46;

        /// <summary>The time column of a recent row.</summary>
        public const double RowTimeWidth = 44;

        /// <summary>Copy / Type-again buttons.</summary>
        public const double RowButtonSize = 32;

        /// <summary>Icon inside a row button.</summary>
        public const double RowIconSize = 15;

        /// <summary>Row icon stroke.</summary>
        public const double RowIconStroke = 1.7;

        /// <summary>The orb mark at the head of Orb's rail.</summary>
        public const double OrbMarkSize = 34;

        /// <summary>The orb in Orb's Home header card.</summary>
        public const double HeroOrbSize = 132;

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
        /// How strongly a caution colour tints the outline of a notice — the "corrected"
        /// chips and the dictionary's false-positive warnings. Caution at full strength
        /// around a box reads as an error; this reads as a note.
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

        /// <summary>
        /// How long the pill stays up after a successful dictation to show its latency —
        /// a glance, not a report.
        /// </summary>
        public static TimeSpan LatencyLinger { get; } = TimeSpan.FromSeconds(1.5);

        /// <summary>
        /// Display gain applied to the raw RMS before the perceptual sqrt. Speech RMS lives
        /// around 0.02–0.15, so without this the meter and HUD bars barely leave the floor.
        /// Display-only — the audio itself is untouched.
        /// </summary>
        public const double LevelGain = 2.5;

        /// <summary>
        /// After minimising for "Type again", how long focus is given to return to the previous
        /// window before the text is sent. Hand-tuned; raise it if the text lands nowhere.
        /// </summary>
        public static TimeSpan RetypeSettle { get; } = TimeSpan.FromMilliseconds(350);
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
