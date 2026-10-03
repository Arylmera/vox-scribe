using VoxScribe.Abstractions;

namespace VoxScribe.Core;

/// <summary>
/// Manages focus anchor logic for dictation: captures the focused field at press,
/// restores focus after injection. Extracted from DictationEngine for testability.
/// </summary>
internal sealed class FocusCapture
{
    private readonly IFocusAnchor? _anchor;
    private Task<IFocusTarget?>? _capture;
    private bool _requested;
    private bool _anchoredThisUtterance;

    /// <summary>Creates an anchor manager with optional focus anchor.</summary>
    public FocusCapture(IFocusAnchor? anchor)
    {
        _anchor = anchor;
    }

    /// <summary>Marks that the next press should capture focus.</summary>
    public void RequestOnNext() => _requested = true;

    /// <summary>Whether focus capture was requested (but not yet captured).</summary>
    public bool IsRequested => _requested;

    /// <summary>Whether this utterance had focus captured.</summary>
    public bool WasAnchored => _anchoredThisUtterance;

    /// <summary>Capture focus asynchronously at key press.</summary>
    public async Task<IFocusTarget?> CaptureAsync(CancellationToken ct)
    {
        if (!_requested || _anchor is null) return null;

        _anchoredThisUtterance = true;
        _capture = _anchor.CaptureAsync(ct);
        return await _capture;
    }

    /// <summary>Restore focus to captured target.</summary>
    public async Task RestoreAsync(CancellationToken ct)
    {
        if (_capture is null) return;
        var target = await _capture;
        if (target is not null) await target.RestoreAsync(ct);
    }

    /// <summary>Reset state for next utterance.</summary>
    public void Reset()
    {
        _requested = false;
        _anchoredThisUtterance = false;
        _capture = null;
    }
}
