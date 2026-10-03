using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A navigation rail key: a square icon button on the left rail.
/// </summary>
/// <remarks>
/// Engaged tints the key with the accent and recolours its stroke icon; otherwise it sits
/// flat on the rail with a silkscreen-grey icon. No borders — the rail separates by tone.
/// </remarks>
public sealed class RailKey : Button
{
    /// <summary>How faintly the accent washes the active key. A tint, not a fill.</summary>
    private const double EngagedWashOpacity = 0.10;

    /// <summary>Whether this key is the active section.</summary>
    public static readonly StyledProperty<bool> IsEngagedProperty =
        AvaloniaProperty.Register<RailKey, bool>(nameof(IsEngaged));

    /// <inheritdoc cref="IsEngagedProperty"/>
    public bool IsEngaged
    {
        get => GetValue(IsEngagedProperty);
        set => SetValue(IsEngagedProperty, value);
    }

    static RailKey() => AffectsRender<RailKey>(IsEngagedProperty, IsPressedProperty);

    /// <summary>Creates a key holding a stroke icon parsed from SVG path data.</summary>
    public RailKey(string iconPathData)
    {
        Width = Tokens.Material.RailKeySize;
        Height = Tokens.Material.RailKeySize;
        // Transparent, never null: a null background is not hit-tested, so only the
        // content — a glyph stroke, a word — would answer the pointer and the rest of the
        // key would silently swallow clicks. The face is painted in Render either way.
        Background = Brushes.Transparent;
        BorderBrush = null;
        Padding = new Thickness(0);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;

        Content = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(iconPathData),
            Stroke = Tokens.Brushes.Silkscreen,
            StrokeThickness = Tokens.Material.RailIconStroke,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = null,
            Width = Tokens.Material.RailIconSize,
            Height = Tokens.Material.RailIconSize,
            Stretch = Stretch.Uniform,
        };
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var shape = new RoundedRect(bounds, Tokens.Radius.RailKey);

        if (IsEngaged)
        {
            context.DrawRectangle(
                new SolidColorBrush(Tokens.Colors.Accent, EngagedWashOpacity), null, shape);
        }
        else if (IsPressed || IsPointerOver)
        {
            context.DrawRectangle(new SolidColorBrush(Tokens.Colors.Hover), null, shape);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Recoloured here, not in Render — see TransportKey for why.
        if (change.Property == IsEngagedProperty
            && Content is Avalonia.Controls.Shapes.Path icon)
        {
            icon.Stroke = IsEngaged
                ? new SolidColorBrush(Tokens.Colors.Accent)
                : Tokens.Brushes.Silkscreen;
        }
    }
}
