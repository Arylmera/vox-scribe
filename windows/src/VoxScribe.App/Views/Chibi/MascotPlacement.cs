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
}
