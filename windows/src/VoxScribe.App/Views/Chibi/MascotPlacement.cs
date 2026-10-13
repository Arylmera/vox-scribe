using Avalonia;

namespace VoxScribe.App.Views.Chibi;

/// <summary>Where the mascot window goes. Pure, so the fallback is testable without screens.</summary>
internal static class MascotPlacement
{
    /// <summary>
    /// The mascot window's bottom-left corner: <paramref name="saved"/> while it is on some
    /// screen, otherwise the bottom-right of the primary working area (<paramref name="areas"/>[0])
    /// inset by <paramref name="margin"/>. A monitor unplugged since the last drag must not
    /// leave the mascot somewhere nobody can see it.
    /// </summary>
    public static PixelPoint Anchor(PixelPoint? saved, IReadOnlyList<PixelRect> areas, PixelSize size, int margin)
    {
        if (areas.Count == 0) return saved ?? default;
        if (saved is { } s && areas.Any(a => a.Contains(new PixelPoint(s.X, s.Y - 1)))) return s;

        var primary = areas[0];
        return new PixelPoint(primary.Right - size.Width - margin, primary.Bottom - margin);
    }

    /// <summary>
    /// True when the bubble should open toward the left of <paramref name="anchor"/> instead of
    /// the right, so it never grows off the edge of the screen. The anchor is in the right half
    /// of the working area that contains it (primary, <paramref name="areas"/>[0], if none does).
    /// </summary>
    public static bool BubbleToLeft(PixelPoint anchor, IReadOnlyList<PixelRect> areas)
    {
        if (areas.Count == 0) return false;
        var area = areas.FirstOrDefault(a => a.Contains(new PixelPoint(anchor.X, anchor.Y - 1)), areas[0]);
        return anchor.X >= area.X + (area.Width / 2);
    }

    /// <summary>
    /// The mascot window's left edge: <paramref name="anchorX"/> when the bubble opens to the
    /// right (today's layout); shifted left by the room the bubble needs when it opens to the
    /// left, so the figure (<paramref name="figureWidth"/> wide) stays pinned to the window's
    /// right edge instead of sliding with it.
    /// </summary>
    public static int WindowX(int anchorX, int windowWidth, int figureWidth, bool bubbleToLeft) =>
        bubbleToLeft ? anchorX - (windowWidth - figureWidth) : anchorX;
}
