namespace VoxScribe.Abstractions;

/// <summary>
/// Where settings, transcripts, the dictionary and the speech model live.
/// </summary>
/// <remarks>
/// The folder was called <c>Murmur</c> until the app was renamed. An installed copy has real
/// user data in there — settings, transcript history, a hand-edited dictionary, and a Parakeet
/// model measured in gigabytes — so the rename moves it rather than starting empty.
/// </remarks>
public static class DataDirectory
{
    private const string Current = "VoxScribe";
    private const string Legacy = "Murmur";

    /// <summary>The data folder, migrated from the old name on first use if need be.</summary>
    public static string Path => Resolve();

    /// <summary>Full path to <paramref name="name"/> inside the data folder.</summary>
    public static string File(params string[] name) =>
        System.IO.Path.Combine([Resolve(), .. name]);

    private static string Resolve()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = System.IO.Path.Combine(root, Current);
        var legacy = System.IO.Path.Combine(root, Legacy);

        // Move once, and only into an absent folder: a half-migrated pair of directories is
        // worse than either one alone, and the running app must never merge them.
        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, current);
            }
            catch (Exception)
            {
                // Locked by another copy of the app, or a permission the user does not have.
                // Fall through: a fresh folder loses settings but still runs, and the old data
                // is left untouched for a second attempt.
            }
        }

        return current;
    }
}
