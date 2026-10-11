using System.Text;

namespace VoxScribe.Core;

/// <summary>
/// Records exactly what was sent to <c>ITextInjector</c> for the current/last
/// dictation — the precise string sequence, concatenated. Foundation for
/// undo-last-dictation (backspace count) and for diff-based correction later
/// (hence <see cref="Retract"/>: "the last N chars were taken back"). Also
/// remembers what a spoken edit replaced, so undo can put it back.
/// Thread-safe: Record is called from the transcription chain, reads from the UI.
/// </summary>
public sealed class InjectionJournal
{
    private readonly StringBuilder _text = new();
    private readonly Lock _lock = new();
    private string? _replaced;

    /// <summary>Starts a new dictation: forget the previous one.</summary>
    public void BeginDictation()
    {
        lock (_lock)
        {
            _text.Clear();
            _replaced = null;
        }
    }

    /// <summary>
    /// Appends one injected string, exactly as sent to the injector.
    /// </summary>
    /// <param name="injected">The text sent to the injector.</param>
    /// <param name="replaced">
    /// When this injection was an edit applied to a selection, the text it replaced — so
    /// undo can put it back. Null leaves whatever was recorded earlier untouched.
    /// </param>
    public void Record(string injected, string? replaced = null)
    {
        lock (_lock)
        {
            _text.Append(injected);
            if (replaced is not null) _replaced = replaced;
        }
    }

    /// <summary>Full injected text of the current/last dictation.</summary>
    public string InjectedText
    {
        get
        {
            lock (_lock)
            {
                return _text.ToString();
            }
        }
    }

    /// <summary>
    /// What the current/last edit replaced, or null when the last dictation was not an
    /// edit applied to a selection. Cleared by <see cref="BeginDictation"/>.
    /// </summary>
    public string? Replaced
    {
        get
        {
            lock (_lock)
            {
                return _replaced;
            }
        }
    }

    /// <summary>The last <paramref name="count"/> chars were deleted again (undo). Clamps.</summary>
    public void Retract(int count)
    {
        lock (_lock)
        {
            if (count <= 0) return;

            _text.Length = Math.Max(0, _text.Length - count);
        }
    }
}
