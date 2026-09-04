using System.Text;

namespace VoxScribe.Core;

/// <summary>
/// Records exactly what was sent to <c>ITextInjector</c> for the current/last
/// dictation — the precise string sequence, concatenated. Foundation for
/// undo-last-dictation (backspace count) and for diff-based correction later
/// (hence <see cref="Retract"/>: "the last N chars were taken back").
/// Thread-safe: Record is called from the transcription chain, reads from the UI.
/// </summary>
public sealed class InjectionJournal
{
    private readonly StringBuilder _text = new();
    private readonly Lock _lock = new();

    /// <summary>Starts a new dictation: forget the previous one.</summary>
    public void BeginDictation()
    {
        lock (_lock)
        {
            _text.Clear();
        }
    }

    /// <summary>Appends one injected string, exactly as sent to the injector.</summary>
    public void Record(string injected)
    {
        lock (_lock)
        {
            _text.Append(injected);
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
