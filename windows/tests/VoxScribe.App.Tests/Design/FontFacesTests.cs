using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Shouldly;
using VoxScribe.App.Design;

namespace VoxScribe.App.Tests.Design;

/// <summary>
/// The bundled fonts are embedded resources. The headless platform draws with a stub font
/// manager, so a glyph lookup would prove nothing; what can fail here is a file missing from
/// the assembly.
/// </summary>
public sealed class FontFacesTests
{
    [AvaloniaFact]
    public void Every_bundled_font_file_is_embedded()
    {
        FontFaces.Files.Count.ShouldBe(16);
        foreach (var file in FontFaces.Files)
        {
            AssetLoader.Exists(new Uri($"avares://VoxScribe.App/{file}")).ShouldBeTrue(file);
        }
    }

    [AvaloniaFact]
    public void Every_family_ships_its_licence()
    {
        foreach (var family in new[] { "InstrumentSerif", "Geist", "GeistMono", "Figtree", "JetBrainsMono" })
        {
            AssetLoader.Exists(new Uri($"avares://VoxScribe.App/Assets/Fonts/{family}/OFL.txt")).ShouldBeTrue(family);
        }
    }
}
