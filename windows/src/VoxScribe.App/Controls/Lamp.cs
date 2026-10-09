using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// An indicator lamp behind a lens.
/// </summary>
/// <remarks>
/// A lit lamp gets a specular dot, not a bloom. The brief rules out glow, and real lamps read
/// as lit because of the highlight on the lens rather than light spilling past it.
/// </remarks>
public sealed class Lamp : Control
{
    /// <summary>Opacity of the rim drawn around the lens.</summary>
    private const double RimOpacity = 0.7;

    /// <summary>Specular dot radius, as a fraction of the lens diameter.</summary>
    private const double DotRadiusRatio = 0.15;

    /// <summary>Where the dot sits, as a fraction of the lens radius — up and to the left.</summary>
    private const double DotOffsetX = 0.30;
    private const double DotOffsetY = 0.32;

    /// <summary>Whether the lamp is lit.</summary>
    public static readonly StyledProperty<bool> IsLitProperty =
        AvaloniaProperty.Register<Lamp, bool>(nameof(IsLit));

    /// <summary>
    /// The lamp's colour when lit. Neutral by default — <b>red means recording</b>, so the
    /// one lamp that means that says so itself rather than inheriting it from every lamp.
    /// </summary>
    public static readonly StyledProperty<Color> LampColorProperty =
        AvaloniaProperty.Register<Lamp, Color>(nameof(LampColor), default);

    /// <inheritdoc cref="IsLitProperty"/>
    public bool IsLit
    {
        get => GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    /// <inheritdoc cref="LampColorProperty"/>
    public Color LampColor
    {
        get => GetValue(LampColorProperty);
        set => SetValue(LampColorProperty, value);
    }

    static Lamp() => AffectsRender<Lamp>(IsLitProperty, LampColorProperty);

    /// <summary>Creates a lamp at the token size.</summary>
    public Lamp()
    {
        LampColor = Tokens.Colors.Silkscreen;
        Width = Tokens.Material.LampSize;
        Height = Tokens.Material.LampSize;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2;

        var lens = new SolidColorBrush(LampColor, IsLit ? 1 : Tokens.Material.LampUnlitOpacity);
        context.DrawEllipse(lens, null, centre, radius, radius);

        var rim = new Pen(new SolidColorBrush(Tokens.Colors.Seam, RimOpacity), Tokens.Border.Hairline);
        context.DrawEllipse(null, rim, centre, radius, radius);

        if (!IsLit) return;

        var specular = new SolidColorBrush(Tokens.Colors.Specular, Tokens.Material.LampSpecular);
        var dot = size * DotRadiusRatio;
        context.DrawEllipse(
            specular, null,
            new Point(centre.X - (radius * DotOffsetX), centre.Y - (radius * DotOffsetY)),
            dot, dot);
    }
}
