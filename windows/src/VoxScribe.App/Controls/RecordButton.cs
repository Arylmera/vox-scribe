using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// The round record button in the voice band: a red-tinted disc holding the record lamp.
/// </summary>
public sealed class RecordButton : Button
{
    /// <summary>How strongly red tints the disc at rest, and while it is held down.</summary>
    private const double DiscOpacity = 0.14;
    private const double PressedDiscOpacity = 0.24;

    static RecordButton() => AffectsRender<RecordButton>(IsPressedProperty);

    /// <summary>Creates the button at the token size.</summary>
    public RecordButton()
    {
        Width = Tokens.Material.RecordKeySize;
        Height = Tokens.Material.RecordKeySize;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        Padding = new Thickness(0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var fill = new SolidColorBrush(
            Tokens.Colors.Record, IsPressed ? PressedDiscOpacity : DiscOpacity);
        context.DrawEllipse(fill, null, centre, size / 2, size / 2);
    }
}
