using System.Text;

namespace VoxScribe.Abstractions;

/// <summary>
/// Whole-file saves that leave either the old file or the new one, never half of either.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Replaces <paramref name="path"/> through a temp file beside it. A reader must never see
    /// half a file, and a crash mid-write must never leave one. A reader that opened the target
    /// without <see cref="FileShare.Delete"/> can briefly block the replace: retried rather than
    /// lost, the same pattern as <c>ReadAloud</c>'s retry on a sharing violation.
    /// </summary>
    /// <param name="path">The file to replace.</param>
    /// <param name="content">The whole new content.</param>
    /// <param name="encoding">Null is UTF-8 without a BOM, the default of <see cref="File.WriteAllText(string, string?)"/>.</param>
    public static void Write(string path, string content, Encoding? encoding = null)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 10)
            {
                Thread.Sleep(10);
            }
        }
    }
}
