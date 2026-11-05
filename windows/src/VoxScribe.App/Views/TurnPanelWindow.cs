using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VoxScribe.App.Design;
using VoxScribe.Core;

namespace VoxScribe.App.Views;

/// <summary>
/// Conversation mode's panel: the code and edits of the last reply, in the corner of the screen.
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

    // The deck is dark or cream with the mode: neither pair reads on the other's ground.
    private static IBrush Added => new SolidColorBrush(Themes.IsDark ? Color.FromRgb(0xA9, 0xDC, 0x76) : Color.FromRgb(0x2E, 0x7D, 0x32));
    private static IBrush Removed => new SolidColorBrush(Themes.IsDark ? Color.FromRgb(0xFF, 0x61, 0x88) : Color.FromRgb(0xC6, 0x28, 0x3F));

    private readonly StackPanel _blocks = new() { Spacing = Tokens.Space.Base };

    /// <summary>Builds the panel, hidden.</summary>
    public TurnPanelWindow()
    {
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

    private static TextBlock Body(PanelBlock block)
    {
        var text = new TextBlock
        {
            FontFamily = Tokens.Fonts.Mono,
            Foreground = Tokens.Brushes.InkOnDeck,
            TextWrapping = TextWrapping.Wrap,
        };

        if (!block.Diff)
        {
            text.Text = block.Body;
            return text;
        }

        text.Inlines = [];
        foreach (var line in block.Body.Split('\n'))
        {
            var run = new Avalonia.Controls.Documents.Run(line + "\n");
            if (line.StartsWith("+ ", StringComparison.Ordinal)) run.Foreground = Added;
            else if (line.StartsWith("- ", StringComparison.Ordinal)) run.Foreground = Removed;
            text.Inlines.Add(run);
        }

        return text;
    }
}
