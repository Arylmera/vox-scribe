using VoxScribe.Abstractions;

namespace VoxScribe.Platform.Windows;

/// <summary>Keys that work as a push-to-talk trigger.</summary>
public enum PushToTalkKey
{
    /// <summary>
    /// Right Ctrl — <b>the default, and the right one.</b>
    /// </summary>
    /// <remarks>
    /// Right Ctrl produces no character on any keyboard layout, so holding it is always safe.
    /// </remarks>
    RightControl = 0xA3,

    /// <summary>Right Shift.</summary>
    RightShift = 0xA1,

    /// <summary>
    /// Right Alt — <b>avoid unless you know the user's layout.</b>
    /// </summary>
    /// <remarks>
    /// On German, Polish, UK, Nordic and most Latin-American layouts this key is AltGr: it is
    /// how those users type <c>@</c>, <c>€</c>, <c>\</c>, <c>|</c> and <c>~</c>. Windows also
    /// synthesises a phantom Left Ctrl around it. It is the natural port of the macOS build's
    /// Right Option, and it is the wrong choice here.
    /// </remarks>
    RightAlt = 0xA5,

    /// <summary>Caps Lock.</summary>
    CapsLock = 0x14,

    /// <summary>F13 — present on many full-size and gaming keyboards, bound to nothing.</summary>
    F13 = 0x7C,
}

/// <summary>
/// Detects press <i>and</i> release of a held chord, globally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>RegisterHotKey</c>:</b> it has no key-up notification at all — the API
/// delivers exactly one <c>WM_HOTKEY</c> on press — and its modifier masks are qualifiers
/// rather than triggers, with no left/right distinction. Neither limitation can be worked
/// around. It is the right API for "Ctrl+Shift+D toggles something" and the wrong one for
/// hold-to-talk.
/// </para>
/// <para>
/// The key stream comes from <see cref="KeyboardHook"/>, the process's single low-level
/// hook. This class holds only the chord state, per instance — the old design kept a
/// callback and a "current instance" in statics, which silently made it a singleton: a
/// second hook overwrote the first and the loser reported success and heard nothing.
/// </para>
/// </remarks>
public sealed class PushToTalkHook : IHotkeySource
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private const int ScanCodeRightShift = 0x36;

    /// <inheritdoc cref="KeyboardHook.InjectedTag"/>
    public static readonly IntPtr InjectedTag = KeyboardHook.InjectedTag;

    /// <summary>The subscription, non-null while listening. Delegate identity is the key.</summary>
    private Action<int, bool>? _listener;

    private volatile bool _isDown;

    /// <summary>Which key triggers dictation. Convenience view over <see cref="Keys"/>.</summary>
    public PushToTalkKey Key
    {
        get => (PushToTalkKey)Keys[0];
        set => Keys = [(int)value];
    }

    /// <summary>
    /// The chord that triggers dictation: pressed when every key is down, released when any
    /// comes up. A single-element array is the classic one-key push-to-talk.
    /// </summary>
    public int[] Keys { get; set; } = [(int)PushToTalkKey.RightControl];

    /// <summary>
    /// Keys that suppress this chord while any of them is held.
    /// </summary>
    /// <remarks>
    /// This is how two overlapping shortcuts coexist. Bind Right Shift for raw dictation and
    /// Left Shift + Right Shift for the cleanup pass, and the raw chord is satisfied by both
    /// gestures — it would fire on every cleanup dictation, and whichever listener happened
    /// to run last would decide what the utterance was. Naming the extra key here makes the
    /// more specific chord win, which is what every shortcut system does and what a user
    /// pressing two keys obviously means.
    /// </remarks>
    public int[] Blockers { get; set; } = [];

    /// <summary>Keys of the chord currently held. Touched only on the hook thread.</summary>
    private readonly HashSet<int> _chordDown = [];

    /// <summary>Blockers currently held. Touched only on the hook thread.</summary>
    private readonly HashSet<int> _blockersDown = [];

    /// <inheritdoc />
    public event EventHandler? Pressed;

    /// <inheritdoc />
    public event EventHandler? Released;

    /// <inheritdoc />
    public bool Start()
    {
        StopListening();

        _listener = OnKey;
        if (KeyboardHook.Subscribe(_listener)) return true;

        _listener = null;
        return false;
    }

    /// <inheritdoc />
    public void StopListening()
    {
        if (_listener is null) return;

        KeyboardHook.Unsubscribe(_listener);
        _listener = null;
        _isDown = false;
        _chordDown.Clear();
        _blockersDown.Clear();
    }

    /// <summary>Runs on the hook thread; keep it short.</summary>
    private void OnKey(int key, bool isDown)
    {
        // The chord can be swapped live while keys from the old one are still down; purge
        // strays here, on the hook thread, so the all-members-down count stays honest.
        _chordDown.RemoveWhere(k => Array.IndexOf(Keys, k) < 0);
        _blockersDown.RemoveWhere(k => Array.IndexOf(Blockers, k) < 0);

        if (Array.IndexOf(Blockers, key) >= 0)
        {
            if (isDown) _blockersDown.Add(key);
            else _blockersDown.Remove(key);

            // Deliberately no release here: a blocker pressed mid-hold must not cut an
            // utterance already being spoken. It only decides whether the next one starts.
        }

        if (Array.IndexOf(Keys, key) < 0) return;

        if (isDown)
        {
            // The OS re-fires key-down while a key is held; Add returns false on repeats.
            if (!_chordDown.Add(key)) return;

            if (_chordDown.Count == Keys.Length && !_isDown && _blockersDown.Count == 0)
            {
                _isDown = true;
                Pressed?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            if (!_chordDown.Remove(key)) return;

            // Any member of the chord coming up ends the hold.
            if (_isDown)
            {
                _isDown = false;
                Released?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Collapses the side-agnostic <c>VK_SHIFT</c>/<c>VK_CONTROL</c>/<c>VK_MENU</c> codes into
    /// left/right-specific ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Physical keys normally arrive already specific, but input injected by another app via
    /// <c>keybd_event</c> or <c>SendInput</c> often uses the neutral form. AutoHotkey — the
    /// most battle-tested low-level keyboard hook in existence — translates defensively for
    /// exactly this reason, and so does this.
    /// </para>
    /// <para>
    /// Right Shift is the awkward one: Microsoft's keyboard-input documentation states it is
    /// <i>not</i> an extended key and is identified by scan code <c>0x36</c>, while
    /// AutoHotkey's source insists it must be treated as extended "or there will be problems".
    /// Both signals are accepted here.
    /// </para>
    /// </remarks>
    internal static int Normalize(int key, int scan, bool extended)
    {
        return key switch
        {
            VK_CONTROL => extended ? VK_RCONTROL : VK_LCONTROL,
            VK_MENU => extended ? VK_RMENU : VK_LMENU,
            VK_SHIFT => scan == ScanCodeRightShift || extended ? VK_RSHIFT : VK_LSHIFT,
            _ => key,
        };
    }

    /// <inheritdoc />
    public void Dispose() => StopListening();
}
