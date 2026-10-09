using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>A one-pixel dashed rule, for log-style lists (Mono). Border has no dashed stroke.</summary>
internal sealed class DashedRule : Control
{
    private const double Dash = 4;
    private const double Gap = 3;

    /// <summary>Creates a rule in the seam colour.</summary>
    public DashedRule()
    {
        Height = Tokens.Border.Hairline;
        Stroke = Tokens.Brushes.Seam;
    }

    /// <summary>The dash colour.</summary>
    public IBrush Stroke { get; set; }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Stroke, Tokens.Border.Hairline, new DashStyle([Dash, Gap], 0));
        var y = Bounds.Height / 2;
        context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
}
