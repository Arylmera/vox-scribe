using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A transport key: a rounded pill button.
/// </summary>
/// <remarks>
/// Engaged tints the pill with the engaged colour; pressed darkens it. Flat and quiet,
/// the Void Glass way.
/// </remarks>
public sealed class TransportKey : Button
{
    /// <summary>How strongly the engaged colour tints the pill's face and its edge.</summary>
    private const double EngagedFillOpacity = 0.16;
    private const double EngagedEdgeOpacity = 0.55;

    /// <summary>How far the face dims while the key is held down.</summary>
    private const double PressedFaceOpacity = 0.6;

    /// <summary>Whether this key is latched down.</summary>
    public static readonly StyledProperty<bool> IsEngagedProperty =
        AvaloniaProperty.Register<TransportKey, bool>(nameof(IsEngaged));

    /// <summary>
    /// Label and tint colour when engaged. Ink by default: a key is latched, not recording,
    /// and a red default made every unconfigured key one <c>IsEngaged</c> away from breaking
    /// the rule that nothing but the transport is red.
    /// </summary>
    public static readonly StyledProperty<Color> EngagedColorProperty =
        AvaloniaProperty.Register<TransportKey, Color>(nameof(EngagedColor), Tokens.Colors.Ink);

    /// <inheritdoc cref="IsEngagedProperty"/>
    public bool IsEngaged
    {
        get => GetValue(IsEngagedProperty);
        set => SetValue(IsEngagedProperty, value);
    }

    /// <inheritdoc cref="EngagedColorProperty"/>
    public Color EngagedColor
    {
        get => GetValue(EngagedColorProperty);
        set => SetValue(EngagedColorProperty, value);
    }

    static TransportKey() => AffectsRender<TransportKey>(IsEngagedProperty, IsPressedProperty);

    /// <summary>Creates a key at the token dimensions.</summary>
    public TransportKey()
    {
        MinWidth = Tokens.Material.KeyMinWidth;
        Height = Tokens.Material.KeyHeight;
        Padding = new Thickness(Tokens.Space.Base, 0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        FontFamily = Tokens.Fonts.Grotesque;
        FontSize = Tokens.Fonts.Silkscreen;
        FontWeight = Avalonia.Media.FontWeight.Medium;
        Foreground = Tokens.Brushes.Ink;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // A full pill: the radius is half the height, whatever the height is.
        var shape = new RoundedRect(bounds, bounds.Height / 2);

        var fill = IsEngaged
            ? new SolidColorBrush(EngagedColor, EngagedFillOpacity)
            : new SolidColorBrush(Tokens.Colors.Cap, IsPressed ? PressedFaceOpacity : 1.0);
        context.DrawRectangle(fill, null, shape);

        var edge = IsEngaged
            ? new SolidColorBrush(EngagedColor, EngagedEdgeOpacity)
            : new SolidColorBrush(Tokens.Colors.Seam);
        context.DrawRectangle(null, new Pen(edge, Tokens.Border.Hairline), shape);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Foreground is updated HERE, not in Render. Assigning a property during the render
        // pass invalidates the visual mid-pass, and Avalonia throws "Visual was invalidated
        // during the render pass" rather than merely logging it. Render must be a pure
        // function of current state.
        if (change.Property == IsEngagedProperty || change.Property == EngagedColorProperty)
        {
            Foreground = new SolidColorBrush(IsEngaged ? EngagedColor : Tokens.Colors.Ink);
        }
    }
}
