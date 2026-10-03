using Avalonia;
using Avalonia.Controls;
using VoxScribe.App.Design;

namespace VoxScribe.App.Controls;

/// <summary>
/// A silkscreened panel label: small, uppercase, tightly tracked.
/// </summary>
/// <remarks>
/// The uppercasing happens here rather than at the call site so a label can never be
/// half-styled — the look depends on all three of size, tracking and case.
/// </remarks>
public sealed class Silkscreen : TextBlock
{
    /// <summary>Uses the larger silkscreen size.</summary>
    public static readonly StyledProperty<bool> IsLargeProperty =
        AvaloniaProperty.Register<Silkscreen, bool>(nameof(IsLarge));

    /// <inheritdoc cref="IsLargeProperty"/>
    public bool IsLarge
    {
        get => GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    /// <summary>Creates an empty label.</summary>
    public Silkscreen()
    {
        FontFamily = Tokens.Fonts.Grotesque;
        FontSize = Tokens.Fonts.Silkscreen;
        FontWeight = Avalonia.Media.FontWeight.Medium;
        LetterSpacing = Tokens.Fonts.SilkscreenTracking;
        Foreground = Tokens.Brushes.Silkscreen;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && Text is { } text)
        {
            var upper = text.ToUpperInvariant();
            if (!string.Equals(text, upper, StringComparison.Ordinal)) Text = upper;
        }
        else if (change.Property == IsLargeProperty)
        {
            FontSize = IsLarge ? Tokens.Fonts.SilkscreenLarge : Tokens.Fonts.Silkscreen;
        }
    }
}
