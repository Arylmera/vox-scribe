using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A glass card: flat translucent surface, soft corners, hairline border.
/// </summary>
/// <remarks>
/// The Void Glass building block — depth comes from the layered surface and its border,
/// never from bevels or texture.
/// </remarks>
public sealed class BrushedPanel : Decorator
{
    /// <summary>Corner radius of the card.</summary>
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<BrushedPanel, double>(nameof(CornerRadius), Tokens.Radius.Panel);

    /// <inheritdoc cref="CornerRadiusProperty"/>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    static BrushedPanel() => AffectsRender<BrushedPanel>(CornerRadiusProperty);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var shape = new RoundedRect(bounds, CornerRadius);
        context.DrawRectangle(Tokens.Brushes.Panel, null, shape);

        var seam = new Pen(new SolidColorBrush(Tokens.Colors.Seam), Tokens.Border.Seam);
        context.DrawRectangle(null, seam, shape);
    }
}
