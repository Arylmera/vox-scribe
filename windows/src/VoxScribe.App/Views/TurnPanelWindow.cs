using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// Conversation mode's panel: the code of the last reply, in the corner of the screen.
/// </summary>
/// <remarks>
/// Like the dictation indicator it must never take focus, or the next dictation would land in
/// nothing. It takes clicks and the wheel, not the keyboard: Escape reaches it through the
/// cancel shortcut, as every other shortcut does, by way of <see cref="ReadAloud.Stop"/>.
/// </remarks>
internal sealed class TurnPanelWindow : Window
{
    private const double PanelWidth = 640;
    private const double PanelHeight = 460;
    private const double ScreenGap = 24;

    private readonly StackPanel _blocks = new() { Spacing = Tokens.Space.Base };

    /// <summary>Raised by a click on the panel, its close button aside.</summary>
    public event EventHandler? Clicked;

    /// <summary>Builds the panel, hidden.</summary>
    public TurnPanelWindow()
    {
        // A button handles its own press, so the close button never reaches this.
        PointerPressed += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);

        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Width = PanelWidth;
        Height = PanelHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Tokens.Brushes.Deck;

        var close = Panels.LinkButton("✕ close");
        close.Focusable = false;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) => Hide();

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Tokens.Colors.Seam),
            BorderThickness = new Thickness(Tokens.Border.Hairline),
            Padding = new Thickness(Tokens.Space.Base),
            Child = new DockPanel
            {
                Children =
                {
                    Panels.Docked(close, Dock.Top),
                    new ScrollViewer { Content = _blocks, Focusable = false },
                },
            },
        };
    }

    /// <summary>The colour of one line of a diff, or null for the plain ink of a context line.</summary>
    /// <remarks>Monokai Pro on the dark deck; on the cream one its tints do not read, so deeper ones.</remarks>
    internal static Color? DiffColour(string line, bool dark)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)) return null;
        if (line.StartsWith('+')) return dark ? Color.FromRgb(0xA9, 0xDC, 0x76) : Color.FromRgb(0x2E, 0x7D, 0x32);
        if (line.StartsWith('-')) return dark ? Color.FromRgb(0xFF, 0x61, 0x88) : Color.FromRgb(0xC6, 0x28, 0x3F);
        if (line.StartsWith("@@", StringComparison.Ordinal)) return dark ? Color.FromRgb(0x78, 0xDC, 0xE8) : Color.FromRgb(0x00, 0x7C, 0x8A);
        return null;
    }

    private static TextBlock Body(PanelBlock block)
    {
        var text = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            Foreground = Tokens.Brushes.InkOnDeck,
            TextWrapping = TextWrapping.Wrap,
        };

        if (!string.Equals(block.Title, "diff", StringComparison.OrdinalIgnoreCase))
        {
            text.Text = block.Body;
            return text;
        }

        text.Inlines = [];
        foreach (var line in block.Body.Split('\n'))
        {
            var run = new Run(line + "\n");
            if (DiffColour(line, Themes.IsDark) is { } colour) run.Foreground = new SolidColorBrush(colour);
            text.Inlines.Add(run);
        }

        return text;
    }

    /// <summary>Shows <paramref name="blocks"/>, or hides the panel when there are none.</summary>
    public void Present(IReadOnlyList<PanelBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            Hide();
            return;
        }

        _blocks.Children.Clear();
        foreach (var block in blocks)
        {
            _blocks.Children.Add(new TextBlock
            {
                Text = block.Title,
                FontFamily = Tokens.Fonts.Mono,
                Foreground = Tokens.Brushes.InkOnDeckAt(Tokens.Emphasis.Soft),
            });
            _blocks.Children.Add(Body(block));
        }

        if (Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Position = new PixelPoint(
                area.Right - (int)((PanelWidth + ScreenGap) * scale),
                area.Bottom - (int)((PanelHeight + ScreenGap) * scale));
        }

        Show();
        Overlay.MakeOverlay(this, clickThrough: false);
    }
}
